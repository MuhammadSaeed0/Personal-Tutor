using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Riwaq.Application.Common;
using Riwaq.Domain.Entities;
using Riwaq.Infrastructure.Persistence;

namespace Riwaq.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "riwaq";
    public string Audience { get; set; } = "riwaq-web";

    /// <summary>HMAC signing key (≥ 32 bytes). Supplied via environment/secret store, never committed.</summary>
    public string? SigningKey { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed record IssuedTokens(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

/// <summary>Issues short-lived JWT access tokens and rotating refresh tokens (stored hashed).</summary>
public sealed class TokenService(AppDbContext db, IOptions<JwtOptions> options, SigningKeyProvider keys, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    public async Task<IssuedTokens> IssueAsync(AppUser user, string? userAgent, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var refresh = NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Ids.New(), UserId = user.Id, TokenHash = Hash(refresh), CreatedAt = now, LastUsedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays), UserAgent = Truncate(userAgent, 300)
        });
        await db.SaveChangesAsync(ct);
        return Build(user, refresh, now);
    }

    /// <summary>
    /// Rotates a refresh token. Presenting an already-rotated token signals theft, so every session of
    /// that user is revoked.
    /// </summary>
    public async Task<(AppUser User, IssuedTokens Tokens)?> RotateAsync(string refreshToken, string? userAgent, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedByHash is not null)
            {
                await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            }
            return null;
        }
        if (stored.ExpiresAt <= now) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null || (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)) return null;

        var next = NewRefreshToken();
        stored.RevokedAt = now;
        stored.ReplacedByHash = Hash(next);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Ids.New(), UserId = user.Id, TokenHash = stored.ReplacedByHash, CreatedAt = stored.CreatedAt, LastUsedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays), UserAgent = Truncate(userAgent, 300)
        });
        await db.SaveChangesAsync(ct);
        return (user, Build(user, next, now));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private IssuedTokens Build(AppUser user, string refresh, DateTime now)
    {
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("name", user.DisplayName)
        };
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, now, expires,
            new SigningCredentials(keys.Key, SecurityAlgorithms.HmacSha256));
        return new IssuedTokens(new JwtSecurityTokenHandler().WriteToken(token), expires, refresh, now.AddDays(_options.RefreshTokenDays));
    }

    private static string NewRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    private static string? Truncate(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}

/// <summary>
/// Supplies the JWT signing key. In Development an ephemeral random key is generated when none is configured
/// (refresh tokens still work across restarts because they live in the database). Production requires a key.
/// </summary>
public sealed class SigningKeyProvider
{
    public SymmetricSecurityKey Key { get; }

    public SigningKeyProvider(IOptions<JwtOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
                throw new InvalidOperationException("Jwt:SigningKey must be configured outside Development.");
            Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(configured);
            if (bytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
            Key = new SymmetricSecurityKey(bytes);
        }
    }
}

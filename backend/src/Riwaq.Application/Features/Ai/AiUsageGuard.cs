using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Riwaq.Application.Abstractions;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Ai;

/// <summary>Per-student daily AI limits and usage accounting.</summary>
public sealed class AiUsageGuard(IAppDbContext db, IConfiguration configuration, TimeProvider clock)
{
    private int DailyRequests => configuration.GetValue("Ai:Limits:DailyRequestsPerUser", 300);
    private int DailyOutputTokens => configuration.GetValue("Ai:Limits:DailyOutputTokensPerUser", 600_000);

    public async Task EnsureAllowedAsync(Guid userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.Date;
        var usage = await db.AiUsage.Where(u => u.UserId == userId && u.CreatedAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Output = g.Sum(u => u.OutputTokens) })
            .FirstOrDefaultAsync(ct);
        if (usage is not null && (usage.Count >= DailyRequests || usage.Output >= DailyOutputTokens))
            throw new AiUnavailableException("ai_quota_exceeded", "You have reached today's AI usage limit. It resets at midnight UTC.");
    }

    public void Record(Guid userId, string feature, ChatResult? result, string model, bool success)
    {
        db.AiUsage.Add(new AiUsageRecord
        {
            UserId = userId,
            Feature = feature,
            Model = result?.Model ?? model,
            InputTokens = result?.InputTokens ?? 0,
            OutputTokens = result?.OutputTokens ?? 0,
            Success = success,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
    }
}

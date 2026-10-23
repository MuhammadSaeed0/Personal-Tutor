using System.Net;

namespace Riwaq.Api.Auth;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string token);
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Base URL of the web app used to build reset links.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4200";
}

/// <summary>
/// Development email sender: writes the reset link to the server log instead of sending mail.
/// Replace with an SMTP or provider-backed implementation for production (see docs/DEPLOYMENT.md).
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, Microsoft.Extensions.Options.IOptions<EmailOptions> options, IHostEnvironment env) : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string token)
    {
        if (env.IsDevelopment())
        {
            var link = $"{options.Value.AppBaseUrl.TrimEnd('/')}/auth/reset-password?email={WebUtility.UrlEncode(email)}&token={WebUtility.UrlEncode(token)}";
            logger.LogInformation("Password reset link (development only): {Link}", link);
        }
        else
        {
            logger.LogWarning("Password reset requested but no email provider is configured.");
        }
        return Task.CompletedTask;
    }
}

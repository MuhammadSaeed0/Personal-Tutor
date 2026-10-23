using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;

namespace Riwaq.Api.Infrastructure;

public static class ClaimsExtensions
{
    /// <summary>The authenticated user's id from the validated token. Never taken from the request body.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : throw new UnauthorizedAccessException();
    }
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Ai = "ai";
    public const string Upload = "upload";
}

/// <summary>
/// CSRF defence for cookie-authenticated endpoints (refresh/logout): browsers cannot attach a custom
/// header to a cross-site request without a CORS preflight, which our CORS policy rejects.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireClientHeaderAttribute : Attribute, IActionFilter
{
    public const string HeaderName = "X-Riwaq-Client";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.Request.Headers.ContainsKey(HeaderName))
            context.Result = new ObjectResult(new ProblemDetails { Status = 400, Title = "Missing client header", Extensions = { ["code"] = "missing_client_header" } }) { StatusCode = 400 };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

/// <summary>Maps exceptions to RFC 7807 problem details with a stable "code" the frontend localises.</summary>
public sealed class ProblemExceptionHandler(ILogger<ProblemExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, title, extensions) = exception switch
        {
            AppException app => (app.StatusCode, app.Code, app.Message, (IDictionary<string, object?>)app.Extensions),
            AiUnavailableException ai => (ai.Code is "ai_rate_limited" or "ai_quota_exceeded" ? 429 : 503, ai.Code, ai.Message, null),
            UnauthorizedAccessException => (401, "unauthorized", "Authentication required.", null),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "cancelled", "Request cancelled.", null),
            BadHttpRequestException bad => (bad.StatusCode, "bad_request", "The request was invalid.", null),
            _ => (500, "server_error", "An unexpected error occurred.", null)
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception on {Path}", context.Request.Path);
        if (status == 499) return true;

        var details = new ProblemDetails { Status = status, Title = title };
        details.Extensions["code"] = code;
        details.Extensions["traceId"] = context.TraceIdentifier;
        if (exception is ValidationException { Errors.Count: > 0 } validation) details.Extensions["errors"] = validation.Errors;
        if (extensions is not null)
            foreach (var (key, value) in extensions) details.Extensions[key] = value;

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details, Exception = exception });
    }
}

[Authorize]
public sealed class RealtimeHub : Hub;

/// <summary>Uses the JWT "sub" claim to address SignalR messages to a user.</summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirstValue("sub");
}

public sealed class SignalRNotifier(IHubContext<RealtimeHub> hub) : IRealtimeNotifier
{
    public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken = default) =>
        hub.Clients.User(userId.ToString()).SendAsync(eventName, payload, cancellationToken);
}

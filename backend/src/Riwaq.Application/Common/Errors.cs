namespace Riwaq.Application.Common;

/// <summary>Base class for expected, user-facing errors. Mapped to RFC 7807 problem details by the API.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code the frontend localises.</summary>
    public string Code { get; } = code;
    public abstract int StatusCode { get; }

    /// <summary>Extra safe data for the client (e.g. the id of an existing duplicate).</summary>
    public Dictionary<string, object?> Extensions { get; } = new();
}

public sealed class NotFoundException(string resource)
    : AppException("not_found", $"{resource} was not found.")
{
    public override int StatusCode => 404;
}

public sealed class ValidationException(string code, string message, IDictionary<string, string[]>? errors = null)
    : AppException(code, message)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
    public override int StatusCode => 400;
}

public sealed class ConflictException(string code, string message) : AppException(code, message)
{
    public override int StatusCode => 409;
}

public sealed class ForbiddenException(string code, string message) : AppException(code, message)
{
    public override int StatusCode => 403;
}

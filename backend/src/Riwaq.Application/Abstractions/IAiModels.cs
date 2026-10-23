namespace Riwaq.Application.Abstractions;

public enum ChatRole
{
    User,
    Assistant
}

public sealed record ChatTurn(ChatRole Role, string Content);

/// <summary>An image sent with the request, e.g. a rendered scanned page. <paramref name="MediaType"/> is image/png or image/jpeg.</summary>
public sealed record ChatImage(string MediaType, byte[] Data);

public sealed record ChatRequest
{
    /// <summary>Trusted application instructions. Untrusted content (textbook text) never goes here.</summary>
    public required string SystemPrompt { get; init; }
    public required IReadOnlyList<ChatTurn> Messages { get; init; }

    /// <summary>Feature name used for usage accounting, e.g. "tutor", "summary", "quiz".</summary>
    public required string Feature { get; init; }
    public int MaxTokens { get; init; } = 8000;

    /// <summary>When set, the provider is asked to return JSON matching this JSON schema.</summary>
    public string? JsonSchema { get; init; }

    /// <summary>Images placed before the text of the last user message (requires <see cref="IChatModel.SupportsImages"/>).</summary>
    public IReadOnlyList<ChatImage>? Images { get; init; }

    /// <summary>Optional effort hint ("low", "medium", "high"). Providers that don't support it ignore it.</summary>
    public string? Effort { get; init; }
}

public sealed record ChatResult(string Text, int InputTokens, int OutputTokens, string Model, string? StopReason);

/// <summary>A streamed piece of a response: either a text delta or, last, the final result.</summary>
public sealed record ChatStreamChunk(string? TextDelta, ChatResult? Final);

/// <summary>Configurable LLM provider (Anthropic Claude, OpenAI-compatible, ...).</summary>
public interface IChatModel
{
    bool IsConfigured { get; }
    string ModelName { get; }

    /// <summary>"anthropic", "claude-account", "openai-compatible" or "none".</summary>
    string Provider { get; }

    /// <summary>Whether the provider accepts <see cref="ChatRequest.Images"/>.</summary>
    bool SupportsImages { get; }
    Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken);
}

public enum EmbeddingPurpose
{
    Document,
    Query
}

public interface IEmbeddingModel
{
    /// <summary>Identifier stored with each vector so vectors from different models are never mixed.</summary>
    string ModelName { get; }
    int Dimensions { get; }

    /// <summary>True for a real semantic model, false for the built-in lexical fallback.</summary>
    bool IsSemantic { get; }
    int MaxBatchSize { get; }
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken);
}

/// <summary>Raised when an AI provider is missing, down, rate limited, or declines a request.</summary>
public sealed class AiUnavailableException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>"ai_not_configured", "ai_unavailable", "ai_rate_limited", "ai_refused", "ai_quota_exceeded", "ai_invalid_output".</summary>
    public string Code { get; } = code;
}

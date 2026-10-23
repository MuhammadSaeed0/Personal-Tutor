using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Ai;

/// <summary>Used when no chat provider is configured. Fails clearly instead of returning fake answers.</summary>
public sealed class UnconfiguredChatModel : IChatModel
{
    public bool IsConfigured => false;
    public string ModelName => "none";
    public string Provider => "none";
    public bool SupportsImages => false;

    public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken cancellationToken) =>
        throw NotConfigured();

    public IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken) =>
        throw NotConfigured();

    private static AiUnavailableException NotConfigured() =>
        new("ai_not_configured", "No AI provider is configured. Set Ai:Chat:Provider (and Ai:Chat:ApiKey for an API provider) on the server.");
}

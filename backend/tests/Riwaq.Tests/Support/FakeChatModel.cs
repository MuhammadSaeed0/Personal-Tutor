using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Riwaq.Application.Abstractions;

namespace Riwaq.Tests.Support;

/// <summary>
/// Test double for the LLM. Responses are produced by a per-feature handler so tests can assert on the
/// exact prompt the application built (retrieved passages, selected text, security framing).
/// </summary>
public sealed class FakeChatModel : IChatModel
{
    private readonly ConcurrentDictionary<string, Func<ChatRequest, string>> _handlers = new();

    public ConcurrentQueue<ChatRequest> Requests { get; } = new();
    public bool IsConfigured => true;
    public string ModelName => "fake-model";
    public string Provider => "fake";
    public bool SupportsImages => true;
    public bool Unavailable { get; set; }

    public void Respond(string feature, Func<ChatRequest, string> handler) => _handlers[feature] = handler;

    public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (Unavailable) throw new AiUnavailableException("ai_unavailable", "Fake provider is down.");
        var text = Handle(request);
        return Task.FromResult(new ChatResult(text, 100, text.Length / 4, ModelName, "end_turn"));
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (Unavailable) throw new AiUnavailableException("ai_unavailable", "Fake provider is down.");
        var text = Handle(request);
        foreach (var piece in Split(text, 12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatStreamChunk(piece, null);
        }
        yield return new ChatStreamChunk(null, new ChatResult(string.Empty, 100, text.Length / 4, ModelName, "end_turn"));
    }

    private string Handle(ChatRequest request) =>
        _handlers.TryGetValue(request.Feature, out var handler)
            ? handler(request)
            : throw new InvalidOperationException($"No fake response registered for feature '{request.Feature}'.");

    private static IEnumerable<string> Split(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size) yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}

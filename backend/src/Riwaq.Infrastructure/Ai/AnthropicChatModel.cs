using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Ai;

/// <summary>Claude via the official Anthropic C# SDK.</summary>
public sealed class AnthropicChatModel : IChatModel
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AnthropicClient _client;
    private readonly ChatProviderOptions _options;
    private readonly ILogger<AnthropicChatModel> _logger;

    public AnthropicChatModel(ChatProviderOptions options, ILogger<AnthropicChatModel> logger)
    {
        _options = options;
        _logger = logger;
        _client = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? new AnthropicClient { ApiKey = options.ApiKey, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) }
            : new AnthropicClient { ApiKey = options.ApiKey, BaseUrl = options.BaseUrl, Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string ModelName => _options.Model;
    public string Provider => "anthropic";

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var parameters = BuildParams(request);
        try
        {
            var message = await _client.Beta.Messages.Create(parameters, cancellationToken);
            var stopReason = message.StopReason?.ToString();
            if (IsRefusal(stopReason))
                throw new AiUnavailableException("ai_refused", "The AI provider declined this request.");

            var text = new StringBuilder();
            foreach (var block in message.Content)
            {
                if (block.TryPickText(out var t)) text.Append(t.Text);
            }

            if (string.Equals(stopReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
                _logger.LogWarning("Claude response for {Feature} hit max_tokens", request.Feature);

            return new ChatResult(text.ToString(), (int)message.Usage.InputTokens, (int)message.Usage.OutputTokens, message.Model.ToString(), stopReason);
        }
        catch (Exception ex) when (ex is not AiUnavailableException and not OperationCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parameters = BuildParams(request);
        var enumerator = _client.Beta.Messages.CreateStreaming(parameters, cancellationToken).GetAsyncEnumerator(cancellationToken);
        long inputTokens = 0, outputTokens = 0;
        string? stopReason = null;
        var model = _options.Model;

        try
        {
            while (true)
            {
                BetaRawMessageStreamEvent ev;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    ev = enumerator.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw Translate(ex);
                }

                if (ev.TryPickStart(out var start))
                {
                    inputTokens = start.Message.Usage.InputTokens;
                    model = start.Message.Model.ToString();
                }
                else if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                {
                    yield return new ChatStreamChunk(text.Text, null);
                }
                else if (ev.TryPickDelta(out var messageDelta))
                {
                    outputTokens = messageDelta.Usage.OutputTokens;
                    stopReason = messageDelta.Delta.StopReason?.ToString();
                    if (IsRefusal(stopReason))
                        throw new AiUnavailableException("ai_refused", "The AI provider declined this request.");
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        yield return new ChatStreamChunk(null, new ChatResult(string.Empty, (int)inputTokens, (int)outputTokens, model, stopReason));
    }

    private MessageCreateParams BuildParams(ChatRequest request)
    {
        var effort = (request.Effort ?? _options.Effort).ToLowerInvariant() switch
        {
            "low" => Effort.Low,
            "high" => Effort.High,
            _ => Effort.Medium
        };

        var outputConfig = new BetaOutputConfig { Effort = effort };
        if (request.JsonSchema is not null)
        {
            outputConfig = new BetaOutputConfig
            {
                Effort = effort,
                Format = new BetaJsonOutputFormat
                {
                    Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.JsonSchema)!
                }
            };
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = request.MaxTokens,
            System = request.SystemPrompt,
            Messages = request.Messages
                .Select(m => m.Role == ChatRole.User
                    ? (BetaMessageParam)new BetaUserMessageParam { Content = m.Content }
                    : new BetaAssistantMessageParam { Content = m.Content })
                .ToList(),
            OutputConfig = outputConfig
        };

        if (_options.UseServerSideFallback)
        {
            parameters = parameters with { Betas = [FallbackBeta], Fallbacks = new Default() };
        }
        return parameters;
    }

    private static bool IsRefusal(string? stopReason) => string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase);

    private AiUnavailableException Translate(Exception ex)
    {
        _logger.LogWarning("Anthropic request failed: {ErrorType}", ex.GetType().Name);
        return ex switch
        {
            AnthropicRateLimitException => new AiUnavailableException("ai_rate_limited", "The AI provider is rate limiting requests. Please retry shortly.", ex),
            AnthropicUnauthorizedException => new AiUnavailableException("ai_not_configured", "The AI provider rejected the configured credentials.", ex),
            _ => new AiUnavailableException("ai_unavailable", "The AI service is temporarily unavailable.", ex)
        };
    }
}

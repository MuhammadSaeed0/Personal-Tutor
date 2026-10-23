using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Ai;

/// <summary>
/// Chat provider for any service exposing the OpenAI Chat Completions API
/// (OpenAI, Azure OpenAI-compatible gateways, Ollama, LM Studio, vLLM...).
/// </summary>
public sealed class OpenAiCompatibleChatModel(HttpClient http, ChatProviderOptions options) : IChatModel
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.BaseUrl);
    public string ModelName => options.Model;
    public string Provider => "openai-compatible";
    public bool SupportsImages => true;

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        using var message = BuildRequest(request, stream: false);
        using var response = await SendAsync(message, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
        var text = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
        var usage = json?["usage"];
        return new ChatResult(text,
            usage?["prompt_tokens"]?.GetValue<int>() ?? 0,
            usage?["completion_tokens"]?.GetValue<int>() ?? 0,
            json?["model"]?.GetValue<string>() ?? options.Model,
            json?["choices"]?[0]?["finish_reason"]?.GetValue<string>());
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var message = BuildRequest(request, stream: true);
        using var response = await SendAsync(message, cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        int input = 0, output = 0;
        string? finish = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch (JsonException) { continue; }

            var delta = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            finish = node?["choices"]?[0]?["finish_reason"]?.GetValue<string>() ?? finish;
            if (node?["usage"] is { } usage && usage.GetValueKind() == JsonValueKind.Object)
            {
                input = usage["prompt_tokens"]?.GetValue<int>() ?? input;
                output = usage["completion_tokens"]?.GetValue<int>() ?? output;
            }
            if (!string.IsNullOrEmpty(delta)) yield return new ChatStreamChunk(delta, null);
        }
        yield return new ChatStreamChunk(null, new ChatResult(string.Empty, input, output, options.Model, finish));
    }

    private HttpRequestMessage BuildRequest(ChatRequest request, bool stream)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt } };
        for (var i = 0; i < request.Messages.Count; i++)
        {
            var m = request.Messages[i];
            JsonNode content = m.Content;
            if (i == request.Messages.Count - 1 && m.Role == ChatRole.User && request.Images is { Count: > 0 } images)
            {
                var parts = new JsonArray();
                foreach (var image in images)
                {
                    parts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data)}" }
                    });
                }
                parts.Add(new JsonObject { ["type"] = "text", ["text"] = m.Content });
                content = parts;
            }
            messages.Add(new JsonObject { ["role"] = m.Role == ChatRole.User ? "user" : "assistant", ["content"] = content });
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream
        };
        if (stream) body["stream_options"] = new JsonObject { ["include_usage"] = true };
        if (request.JsonSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "result", ["schema"] = JsonNode.Parse(request.JsonSchema), ["strict"] = true }
            };
        }

        var url = options.BaseUrl!.TrimEnd('/') + "/chat/completions";
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return message;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, completion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("ai_unavailable", "The AI service could not be reached.", ex);
        }

        if (response.IsSuccessStatusCode) return response;
        var code = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "ai_rate_limited",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "ai_not_configured",
            _ => "ai_unavailable"
        };
        response.Dispose();
        throw new AiUnavailableException(code, $"The AI service returned {(int)response.StatusCode}.");
    }
}

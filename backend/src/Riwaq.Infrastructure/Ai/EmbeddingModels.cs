using System.IO.Hashing;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Text;

namespace Riwaq.Infrastructure.Ai;

/// <summary>
/// Built-in lexical embedding: hashes word tokens and character n-grams into a fixed-size vector.
/// It needs no API key and gives useful keyword-level similarity, but it is not a semantic model;
/// configure Voyage or an OpenAI-compatible embedding endpoint for meaning-based retrieval.
/// </summary>
public sealed class LocalHashingEmbeddingModel : IEmbeddingModel
{
    private const int Dims = 768;

    public string ModelName => "local-hash-v1";
    public int Dimensions => Dims;
    public bool IsSemantic => false;
    public int MaxBatchSize => 512;

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult(texts.Select(Embed).ToArray());

    public static float[] Embed(string text)
    {
        var vector = new float[Dims];
        foreach (var token in SearchTokenizer.Tokenize(text))
        {
            Add(vector, token, 1.0f);
            // Character trigrams make the vector tolerant to Arabic morphology and inflection.
            var padded = $"#{token}#";
            for (var i = 0; i + 3 <= padded.Length; i++) Add(vector, padded.Substring(i, 3), 0.35f);
        }

        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    private static void Add(float[] vector, string feature, float weight)
    {
        var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(feature));
        var index = (int)(hash % Dims);
        var sign = (hash & 0x80000000) == 0 ? 1f : -1f;
        vector[index] += sign * weight;
    }
}

/// <summary>Voyage AI embeddings (recommended pairing for Claude).</summary>
public sealed class VoyageEmbeddingModel(HttpClient http, EmbeddingProviderOptions options) : IEmbeddingModel
{
    public string ModelName => $"voyage:{options.Model}";
    public int Dimensions => options.Dimensions;
    public bool IsSemantic => true;
    public int MaxBatchSize => Math.Clamp(options.BatchSize, 1, 128);

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["input"] = new JsonArray(texts.Select(t => (JsonNode)t).ToArray()),
            ["input_type"] = purpose == EmbeddingPurpose.Query ? "query" : "document",
            ["output_dimension"] = options.Dimensions
        };
        var url = (options.BaseUrl ?? "https://api.voyageai.com/v1").TrimEnd('/') + "/embeddings";
        return await EmbeddingHttp.PostAsync(http, url, options.ApiKey, body, cancellationToken);
    }
}

/// <summary>OpenAI-compatible /embeddings endpoint (OpenAI, Ollama, LM Studio, ...).</summary>
public sealed class OpenAiCompatibleEmbeddingModel(HttpClient http, EmbeddingProviderOptions options) : IEmbeddingModel
{
    public string ModelName => $"openai:{options.Model}";
    public int Dimensions => options.Dimensions;
    public bool IsSemantic => true;
    public int MaxBatchSize => Math.Clamp(options.BatchSize, 1, 256);

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["input"] = new JsonArray(texts.Select(t => (JsonNode)t).ToArray())
        };
        var url = (options.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/') + "/embeddings";
        return await EmbeddingHttp.PostAsync(http, url, options.ApiKey, body, cancellationToken);
    }
}

internal static class EmbeddingHttp
{
    public static async Task<float[][]> PostAsync(HttpClient http, string url, string? apiKey, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("ai_unavailable", "The embedding service could not be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException((int)response.StatusCode == 429 ? "ai_rate_limited" : "ai_unavailable",
                    $"The embedding service returned {(int)response.StatusCode}.");

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var data = json?["data"]?.AsArray() ?? throw new AiUnavailableException("ai_invalid_output", "Unexpected embedding response.");
            return data
                .OrderBy(d => d?["index"]?.GetValue<int>() ?? 0)
                .Select(d => d!["embedding"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray())
                .ToArray();
        }
    }
}

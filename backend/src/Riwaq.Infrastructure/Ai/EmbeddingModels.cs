using System.IO.Hashing;
using System.Text;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Text;

namespace Riwaq.Infrastructure.Ai;

/// <summary>
/// Built-in lexical embedding: hashes word tokens and character n-grams into a fixed-size vector.
/// It needs no API key and gives useful keyword-level similarity, but it is not a semantic model;
/// it is the fallback when the local semantic model is not available.
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

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Ai;

/// <summary>
/// Semantic embeddings computed on this machine with multilingual-e5-small (int8 ONNX, ~120 MB, 384 dimensions).
/// It understands Arabic, English and French by meaning, needs no key and makes no network calls.
/// Files: <c>model_qint8_avx512_vnni.onnx</c> and <c>tokenizer.json</c> from Hugging Face intfloat/multilingual-e5-small
/// (see scripts/get-model.ps1).
/// </summary>
public sealed class OnnxE5EmbeddingModel : IEmbeddingModel, IDisposable
{
    public const string ModelFile = "model_qint8_avx512_vnni.onnx";
    public const string TokenizerFile = "tokenizer.json";

    // XLM-RoBERTa special tokens (ids from the model's tokenizer.json).
    private const long Bos = 0, Pad = 1, Eos = 2;
    private const int MaxTokens = 512;

    private readonly InferenceSession _session;
    private readonly SentencePieceTokenizer _tokenizer;
    private readonly bool _needsTokenTypes;
    private readonly string _outputName;

    public OnnxE5EmbeddingModel(string directory)
    {
        using (var json = File.OpenRead(Path.Combine(directory, TokenizerFile)))
            _tokenizer = SentencePieceTokenizer.CreateFromTokenizerJson(json, addBeginningOfSentence: false, addEndOfSentence: false);

        // Leave half the cores to the web server and the database while books are being indexed.
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
        };
        _session = new InferenceSession(Path.Combine(directory, ModelFile), options);
        _needsTokenTypes = _session.InputMetadata.ContainsKey("token_type_ids");
        _outputName = _session.OutputMetadata.Keys.Contains("last_hidden_state") ? "last_hidden_state" : _session.OutputMetadata.Keys.First();
    }

    /// <summary>True when both model files are present in <paramref name="directory"/>.</summary>
    public static bool IsAvailable(string directory) =>
        File.Exists(Path.Combine(directory, ModelFile)) && File.Exists(Path.Combine(directory, TokenizerFile));

    public string ModelName => "multilingual-e5-small-q8";
    public int Dimensions => 384;
    public bool IsSemantic => true;
    public int MaxBatchSize => 16;

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken)
    {
        // The model was trained with these prefixes; leaving them out noticeably lowers retrieval quality.
        var prefix = purpose == EmbeddingPurpose.Query ? "query: " : "passage: ";
        var tokens = texts.Select(t => Tokenize(prefix + t)).ToArray();
        var results = new float[texts.Count][];

        // Similar lengths together so a batch carries little padding.
        var order = Enumerable.Range(0, texts.Count).OrderBy(i => tokens[i].Length).ToArray();
        foreach (var batch in order.Chunk(MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vectors = Run(batch.Select(i => tokens[i]).ToArray());
            for (var k = 0; k < batch.Length; k++) results[batch[k]] = vectors[k];
        }
        return Task.FromResult(results);
    }

    internal long[] Tokenize(string text)
    {
        var ids = _tokenizer.EncodeToIds(text, false, false, MaxTokens - 2, out _, out _);
        var tokens = new long[ids.Count + 2];
        tokens[0] = Bos;
        for (var i = 0; i < ids.Count; i++) tokens[i + 1] = ids[i];
        tokens[^1] = Eos;
        return tokens;
    }

    private float[][] Run(long[][] batch)
    {
        var rows = batch.Length;
        var length = batch.Max(t => t.Length);
        var inputIds = new DenseTensor<long>([rows, length]);
        var mask = new DenseTensor<long>([rows, length]);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < length; c++)
            {
                var real = c < batch[r].Length;
                inputIds[r, c] = real ? batch[r][c] : Pad;
                mask[r, c] = real ? 1 : 0;
            }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask)
        };
        if (_needsTokenTypes) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>([rows, length])));

        using var output = _session.Run(inputs, [_outputName]);
        var hidden = output[0].AsTensor<float>(); // [rows, length, 384]
        var dims = (int)hidden.Dimensions[2];

        // Mean of the token vectors (padding excluded), then unit length so a dot product is cosine similarity.
        var vectors = new float[rows][];
        for (var r = 0; r < rows; r++)
        {
            var sum = new double[dims];
            var count = batch[r].Length;
            for (var c = 0; c < count; c++)
                for (var d = 0; d < dims; d++) sum[d] += hidden[r, c, d];
            var norm = Math.Sqrt(sum.Sum(v => (v / count) * (v / count)));
            var vector = new float[dims];
            for (var d = 0; d < dims; d++) vector[d] = norm > 0 ? (float)(sum[d] / count / norm) : 0f;
            vectors[r] = vector;
        }
        return vectors;
    }

    public void Dispose() => _session.Dispose();
}

using Riwaq.Application.Abstractions;
using Riwaq.Application.Ingestion;
using Riwaq.Infrastructure.Ai;

namespace Riwaq.Tests.Unit;

/// <summary>Runs only where the model was downloaded (scripts/get-model.ps1).</summary>
public sealed class OnnxModelFactAttribute : FactAttribute
{
    public OnnxModelFactAttribute()
    {
        if (OnnxEmbeddingTests.ModelDirectory is null) Skip = "Semantic model not downloaded (run scripts/get-model.ps1).";
    }
}

public class OnnxEmbeddingTests
{
    internal static readonly string? ModelDirectory = Find();

    private static string? Find()
    {
        var configured = Environment.GetEnvironmentVariable("RIWAQ_E5_MODEL");
        if (!string.IsNullOrEmpty(configured)) return OnnxE5EmbeddingModel.IsAvailable(configured) ? configured : null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Riwaq.Api", "App_Data", "models", "multilingual-e5-small");
            if (OnnxE5EmbeddingModel.IsAvailable(candidate)) return candidate;
        }
        return null;
    }

    private static readonly Lazy<OnnxE5EmbeddingModel> Model = new(() => new OnnxE5EmbeddingModel(ModelDirectory!));

    [OnnxModelFact]
    public void Tokens_are_wrapped_in_sentence_markers_and_capped()
    {
        var ids = Model.Value.Tokenize("passage: " + string.Concat(Enumerable.Repeat("قانون نيوتن الثاني ", 400)));
        Assert.Equal(512, ids.Length);
        Assert.Equal(0, ids[0]);
        Assert.Equal(2, ids[^1]);
        Assert.DoesNotContain(3L, Model.Value.Tokenize("query: ما هو التمثيل الضوئي؟")); // no unknown tokens in Arabic
    }

    [OnnxModelFact]
    public async Task Ranks_passages_by_meaning_across_wording_and_language()
    {
        var model = Model.Value;
        var query = (await model.EmbedAsync(["ما العلاقة بين القوة والتسارع؟"], EmbeddingPurpose.Query, CancellationToken.None))[0];
        var passages = await model.EmbedAsync(
        [
            "ينص قانون نيوتن الثاني على أن محصلة القوى المؤثرة على جسم تساوي حاصل ضرب كتلته في عجلته.",
            "Newton's second law: the net force on a body equals its mass times its acceleration.",
            "تقوم النباتات الخضراء بعملية البناء الضوئي لتحويل الطاقة الضوئية إلى طاقة كيميائية.",
            "تتكون الصخور الرسوبية من ترسيب الفتات الصخري في طبقات."
        ], EmbeddingPurpose.Document, CancellationToken.None);

        Assert.All(passages, p => Assert.Equal(384, p.Length));
        Assert.Equal(1.0, Math.Sqrt(passages[0].Sum(v => (double)v * v)), 3);
        var scores = passages.Select(p => VectorCodec.Cosine(query, p)).ToArray();
        Assert.True(scores[0] > scores[2] && scores[0] > scores[3], string.Join(", ", scores));
        Assert.True(scores[1] > scores[2] && scores[1] > scores[3], string.Join(", ", scores));
    }

    [OnnxModelFact]
    public async Task Batching_barely_changes_vectors()
    {
        var texts = new[] { "قصير", "نص أطول قليلًا عن الخلية الحيوانية ومكوناتها الأساسية مثل النواة والسيتوبلازم" };
        var together = await Model.Value.EmbedAsync(texts, EmbeddingPurpose.Document, CancellationToken.None);
        var alone = await Model.Value.EmbedAsync([texts[0]], EmbeddingPurpose.Document, CancellationToken.None);
        // The int8 model quantises activations per batch, so padding shifts results by a hair (about 0.995).
        var similarity = VectorCodec.Cosine(together[0], alone[0]);
        Assert.True(similarity > 0.99, similarity.ToString());
    }
}

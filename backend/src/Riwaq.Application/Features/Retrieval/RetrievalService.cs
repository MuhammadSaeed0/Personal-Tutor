using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Ingestion;
using Riwaq.Application.Text;
using Riwaq.Domain;

namespace Riwaq.Application.Features.Retrieval;

public sealed record RetrievalQuery(
    IReadOnlyList<Guid> BookIds,
    string Text,
    IReadOnlyList<int>? Pages = null,
    Guid? ChapterId = null,
    int TopK = 8);

public sealed record RetrievedPassage(
    long ChunkId,
    Guid BookId,
    string BookTitle,
    Guid? ChapterId,
    string? ChapterTitle,
    int PageStart,
    int PageEnd,
    string Text,
    double Score,
    string MatchType);

/// <summary>
/// Hybrid retrieval over a student's own books: BM25 keyword scoring plus vector similarity, fused with
/// reciprocal-rank fusion. Explicit page references are always honoured first. Every query is restricted
/// to books owned by the requesting user.
/// </summary>
public sealed class RetrievalService(
    IAppDbContext db,
    IEmbeddingModel embedder,
    IMemoryCache cache,
    ILogger<RetrievalService> logger)
{
    private const double K1 = 1.4, B = 0.75, RrfK = 60;

    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(Guid userId, RetrievalQuery query, CancellationToken ct)
    {
        var books = await db.Books.AsNoTracking()
            .Where(b => b.UserId == userId && query.BookIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Title, b.ContentVersion })
            .ToListAsync(ct);
        if (books.Count == 0) return [];

        var indexes = new List<BookIndex>();
        foreach (var book in books) indexes.Add(await GetIndexAsync(book.Id, book.Title, book.ContentVersion, ct));

        var all = indexes.SelectMany(i => i.Chunks.Select(c => (Index: i, Chunk: c))).ToList();
        if (query.ChapterId is not null)
        {
            var inChapter = all.Where(x => x.Chunk.ChapterId == query.ChapterId).ToList();
            if (inChapter.Count > 0) all = inChapter;
        }
        if (all.Count == 0) return [];

        var results = new Dictionary<long, (IndexedChunk Chunk, BookIndex Index, double Score, string Type)>();

        // 1. Explicit pages ("explain page 42") are included first.
        if (query.Pages is { Count: > 0 })
        {
            foreach (var x in all.Where(x => query.Pages.Any(p => p >= x.Chunk.PageStart && p <= x.Chunk.PageEnd)).Take(query.TopK))
                results[x.Chunk.Id] = (x.Chunk, x.Index, 1.0, "page");
        }

        // 2. Keyword (BM25) ranking.
        var queryTokens = SearchTokenizer.Tokenize(query.Text).Distinct().ToList();
        var bm25 = queryTokens.Count == 0 ? [] : RankBm25(all, queryTokens);

        // 3. Vector ranking (only with vectors from the configured model).
        var vectorRank = new List<(IndexedChunk Chunk, BookIndex Index, double Score)>();
        if (indexes.Any(i => i.HasVectors) && !string.IsNullOrWhiteSpace(query.Text))
        {
            try
            {
                var q = (await embedder.EmbedAsync([query.Text], EmbeddingPurpose.Query, ct))[0];
                vectorRank = all.Where(x => x.Chunk.Vector is not null)
                    .Select(x => (x.Chunk, x.Index, Score: (double)VectorCodec.Cosine(q, x.Chunk.Vector)))
                    .Where(x => x.Score > (embedder.IsSemantic ? 0.2 : 0.05))
                    .OrderByDescending(x => x.Score).Take(50).ToList();
            }
            catch (AiUnavailableException ex)
            {
                logger.LogWarning("Query embedding failed ({Code}); using keyword retrieval only", ex.Code);
            }
        }

        // 4. Reciprocal-rank fusion.
        var fused = new Dictionary<long, (IndexedChunk Chunk, BookIndex Index, double Score, string Type)>();
        void Fuse(IEnumerable<(IndexedChunk Chunk, BookIndex Index, double Score)> ranked, string type)
        {
            var rank = 0;
            foreach (var (chunk, index, _) in ranked)
            {
                rank++;
                var add = 1.0 / (RrfK + rank);
                fused[chunk.Id] = fused.TryGetValue(chunk.Id, out var existing)
                    ? (chunk, index, existing.Score + add, existing.Type == type ? type : "hybrid")
                    : (chunk, index, add, type);
            }
        }
        Fuse(bm25, "keyword");
        Fuse(vectorRank, "semantic");

        foreach (var (id, value) in fused.OrderByDescending(f => f.Value.Score))
        {
            if (results.Count >= query.TopK) break;
            results.TryAdd(id, value);
        }

        return results.Values
            .Select(r => new RetrievedPassage(r.Chunk.Id, r.Index.BookId, r.Index.BookTitle, r.Chunk.ChapterId,
                r.Chunk.ChapterId is { } cid ? r.Index.ChapterTitles.GetValueOrDefault(cid) : null,
                r.Chunk.PageStart, r.Chunk.PageEnd, r.Chunk.Text, Math.Round(r.Score, 5), r.Type))
            .ToList();
    }

    private static List<(IndexedChunk Chunk, BookIndex Index, double Score)> RankBm25(
        List<(BookIndex Index, IndexedChunk Chunk)> corpus, List<string> queryTokens)
    {
        var n = corpus.Count;
        var avgLength = corpus.Average(x => (double)x.Chunk.Length);
        var df = queryTokens.ToDictionary(t => t, t => corpus.Count(x => x.Chunk.TermFrequency.ContainsKey(t)));

        return corpus
            .Select(x =>
            {
                double score = 0;
                foreach (var token in queryTokens)
                {
                    if (!x.Chunk.TermFrequency.TryGetValue(token, out var tf)) continue;
                    var idf = Math.Log(1 + (n - df[token] + 0.5) / (df[token] + 0.5));
                    score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * x.Chunk.Length / avgLength));
                }
                return (x.Chunk, x.Index, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(50)
            .ToList();
    }

    private async Task<BookIndex> GetIndexAsync(Guid bookId, string title, int version, CancellationToken ct)
    {
        var key = $"rag:{bookId}:{version}:{embedder.ModelName}";
        if (cache.TryGetValue(key, out BookIndex? cached) && cached is not null) return cached with { BookTitle = title };

        var chunks = await db.DocumentChunks.AsNoTracking().Where(c => c.BookId == bookId)
            .OrderBy(c => c.Ordinal)
            .Select(c => new { c.Id, c.ChapterId, c.PageStart, c.PageEnd, c.Text })
            .ToListAsync(ct);
        var vectors = await db.ChunkEmbeddings.AsNoTracking().Where(e => e.BookId == bookId && e.Model == embedder.ModelName)
            .Select(e => new { e.ChunkId, e.Vector })
            .ToDictionaryAsync(e => e.ChunkId, e => e.Vector, ct);
        var chapterTitles = await db.Chapters.AsNoTracking().Where(c => c.BookId == bookId)
            .ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        var indexed = chunks.Select(c =>
        {
            var tokens = SearchTokenizer.Tokenize(c.Text);
            return new IndexedChunk(c.Id, c.ChapterId, c.PageStart, c.PageEnd, c.Text,
                tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()),
                Math.Max(1, tokens.Count),
                vectors.TryGetValue(c.Id, out var v) ? VectorCodec.FromBytes(v) : null);
        }).ToList();

        var index = new BookIndex(bookId, title, indexed, chapterTitles, vectors.Count > 0);
        cache.Set(key, index, new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(30)
        });
        return index;
    }

    private sealed record IndexedChunk(long Id, Guid? ChapterId, int PageStart, int PageEnd, string Text,
        Dictionary<string, int> TermFrequency, int Length, float[]? Vector);

    private sealed record BookIndex(Guid BookId, string BookTitle, List<IndexedChunk> Chunks,
        Dictionary<Guid, string> ChapterTitles, bool HasVectors);
}

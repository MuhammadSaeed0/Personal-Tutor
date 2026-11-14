using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Features.Retrieval;
using Riwaq.Application.Ingestion;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Infrastructure.Background;

/// <summary>
/// After the embedding model changes, adds vectors from the current model to sources that were indexed before.
/// It reads the stored text chunks only (no re-extraction, OCR or Claude calls). Until a source is done,
/// search for it falls back to keyword ranking.
/// </summary>
public sealed class EmbeddingBackfillWorker(
    IServiceScopeFactory scopes,
    IEmbeddingModel embedder,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<EmbeddingBackfillWorker> logger) : BackgroundService
{
    private const int BatchSize = 32;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!embedder.IsSemantic) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // let startup and migrations finish first
            await BackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Embedding backfill stopped; it resumes on the next start.");
        }
    }

    internal async Task BackfillAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var model = embedder.ModelName;

        var books = await db.Books.AsNoTracking()
            .Where(b => (b.Status == BookStatus.Ready || b.Status == BookStatus.ReadyWithWarnings)
                        && db.DocumentChunks.Any(c => c.BookId == b.Id
                                                      && !db.ChunkEmbeddings.Any(e => e.ChunkId == c.Id && e.Model == model)))
            .Select(b => new { b.Id, b.Title })
            .ToListAsync(ct);
        if (books.Count == 0) return;
        logger.LogInformation("Adding {Model} search vectors to {Count} source(s).", model, books.Count);

        foreach (var book in books)
        {
            try
            {
                var added = await BackfillBookAsync(db, book.Id, model, ct);
                logger.LogInformation("Search vectors ready for \"{Title}\" ({Count} passages).", book.Title, added);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Usually the source is being reprocessed right now, which embeds it anyway.
                logger.LogWarning(ex, "Skipped search vectors for source {BookId}.", book.Id);
                ((DbContext)db).ChangeTracker.Clear();
            }
        }
    }

    private async Task<int> BackfillBookAsync(IAppDbContext db, Guid bookId, string model, CancellationToken ct)
    {
        var chunks = await db.DocumentChunks.AsNoTracking()
            .Where(c => c.BookId == bookId && !db.ChunkEmbeddings.Any(e => e.ChunkId == c.Id && e.Model == model))
            .OrderBy(c => c.Ordinal)
            .Select(c => new { c.Id, c.Text, c.ContentHash })
            .ToListAsync(ct);

        foreach (var batch in chunks.Chunk(BatchSize))
        {
            var vectors = await embedder.EmbedAsync(batch.Select(c => c.Text).ToList(), EmbeddingPurpose.Document, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            for (var i = 0; i < batch.Length; i++)
                db.ChunkEmbeddings.Add(new ChunkEmbedding
                {
                    ChunkId = batch[i].Id, BookId = bookId, Model = model, Dimensions = vectors[i].Length,
                    Vector = VectorCodec.ToBytes(vectors[i]), ContentHash = batch[i].ContentHash, CreatedAt = now
                });
            await db.SaveChangesAsync(ct);
            ((DbContext)db).ChangeTracker.Clear();
        }

        // The cached search index was built without these vectors.
        var version = await db.Books.Where(b => b.Id == bookId).Select(b => b.ContentVersion).FirstAsync(ct);
        cache.Remove(RetrievalService.IndexCacheKey(bookId, version, model));
        return chunks.Count;
    }
}

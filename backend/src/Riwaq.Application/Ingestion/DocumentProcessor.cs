using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Books;
using Riwaq.Application.Text;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Ingestion;

public sealed class ProcessingOptions
{
    public const string Section = "Processing";
    public int MaxFileSizeMb { get; set; } = 150;
    public int MaxPages { get; set; } = 2000;

    /// <summary>A page with fewer extracted letters than this is treated as scanned/image-only.</summary>
    public int ScannedLetterThreshold { get; set; } = 25;
    public double OcrLowConfidence { get; set; } = 60;
    public int EmbeddingBatchSize { get; set; } = 64;
}

/// <summary>Raised for document problems that retrying cannot fix (corrupt or encrypted PDF).</summary>
public sealed class PermanentProcessingException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Runs the ingestion pipeline for one job: validate → extract → OCR → structure → tables/figures →
/// chunk + embed. Every stage is persisted so progress survives restarts and is pushed to the student.
/// </summary>
public sealed class DocumentProcessor(
    IAppDbContext db,
    IFileStorage storage,
    IPdfExtractor extractor,
    IOcrEngine ocr,
    IEmbeddingModel embedder,
    IRealtimeNotifier notifier,
    IOptions<ProcessingOptions> options,
    TimeProvider clock,
    ILogger<DocumentProcessor> logger)
{
    private readonly ProcessingOptions _options = options.Value;

    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.ProcessingJobs.FirstAsync(j => j.Id == jobId, ct);
        var book = await db.Books.Include(b => b.Subject).FirstAsync(b => b.Id == job.BookId, ct);
        var warnings = new List<DocumentWarning>();

        book.Status = BookStatus.Processing;
        await Stage(job, book, ProcessingStage.Validating, 5, ct);

        var path = await storage.GetLocalPathAsync(book.StorageKey, ct);
        var validation = extractor.Validate(path);
        if (!validation.IsValid) throw new PermanentProcessingException(validation.ErrorCode ?? "pdf_unreadable");
        if (validation.PageCount > _options.MaxPages) throw new PermanentProcessingException("pdf_too_many_pages");

        await Stage(job, book, ProcessingStage.ExtractingText, 10, ct);
        var document = await Task.Run(() => extractor.Extract(path, ct), ct);
        book.PageCount = document.PageCount;

        // Pages: embedded text first, OCR for scanned pages.
        var scannedPages = document.Pages.Where(p => p.LetterCount < _options.ScannedLetterThreshold).ToList();
        var ocrText = new Dictionary<int, string>();
        if (scannedPages.Count > 0)
        {
            await Stage(job, book, ProcessingStage.RecognizingScannedPages, 30, ct);
            if (ocr.IsAvailable)
            {
                var languages = OcrLanguages(book.Subject?.ContentLanguage);
                var done = 0;
                foreach (var page in scannedPages)
                {
                    var result = await ocr.RecognizePageAsync(path, page.Number, languages, ct);
                    if (result.Success)
                    {
                        ocrText[page.Number] = TextCleaner.CleanExtracted(result.Text);
                        if (result.MeanConfidence < _options.OcrLowConfidence)
                            warnings.Add(Warning(book.Id, page.Number, "ocr_low_confidence", WarningSeverity.Warning,
                                $"OCR confidence {result.MeanConfidence:0}% — text on this page may contain recognition errors."));
                    }
                    else
                    {
                        warnings.Add(Warning(book.Id, page.Number, "ocr_failed", WarningSeverity.Warning, "Text on this scanned page could not be recognised."));
                    }

                    if (++done % 5 == 0) await Stage(job, book, ProcessingStage.RecognizingScannedPages, 30 + 20 * done / scannedPages.Count, ct);
                }
            }
            else
            {
                foreach (var page in scannedPages)
                    warnings.Add(Warning(book.Id, page.Number, "scanned_page_no_ocr", WarningSeverity.Warning,
                        "This page has no embedded text and OCR is not available on the server."));
            }
        }

        // Reprocessing: keep existing vectors by content hash so unchanged text isn't re-embedded.
        var reusableVectors = await db.ChunkEmbeddings
            .Where(e => e.BookId == book.Id && e.Model == embedder.ModelName)
            .Select(e => new { e.ContentHash, e.Vector, e.Dimensions })
            .ToListAsync(ct);
        var vectorByHash = reusableVectors.GroupBy(v => v.ContentHash).ToDictionary(g => g.Key, g => g.First().Vector);

        // Titles the student corrected are re-applied to chapters detected at the same start page and level.
        var corrections = await db.Chapters
            .Where(c => c.BookId == book.Id && c.Source == ChapterSource.User)
            .Select(c => new { c.StartPage, c.Level, c.Title })
            .ToListAsync(ct);

        await ClearDerivedContentAsync(book.Id, ct);

        var pageEntities = document.Pages.Select(p =>
        {
            var hasOcr = ocrText.TryGetValue(p.Number, out var recognised);
            var text = hasOcr ? recognised! : p.Text;
            return new DocumentPage
            {
                BookId = book.Id,
                PageNumber = p.Number,
                Text = text,
                CharCount = text.Length,
                IsScanned = p.LetterCount < _options.ScannedLetterThreshold,
                ExtractionMethod = hasOcr ? ExtractionMethod.Ocr : text.Length > 0 ? ExtractionMethod.EmbeddedText : ExtractionMethod.None,
                ImageCount = p.ImageCount,
                HasMathNotation = TextCleaner.LooksMathematical(text),
                HasTableLikeLayout = TextCleaner.LooksTabular(p.Lines.Select(l => l.Text))
            };
        }).ToList();
        db.DocumentPages.AddRange(pageEntities);

        var allText = string.Join('\n', pageEntities.Take(40).Select(p => p.Text));
        book.DetectedLanguage = LanguageDetector.Detect(allText);

        await Stage(job, book, ProcessingStage.DetectingStructure, 55, ct);
        var sections = StructureDetector.Detect(document);
        var chapters = new List<Chapter>();
        Chapter? currentTop = null;
        foreach (var (section, index) in sections.Select((s, i) => (s, i)))
        {
            var chapter = new Chapter
            {
                Id = Ids.New(),
                BookId = book.Id,
                Title = section.Title,
                DetectedTitle = section.Title,
                Level = section.Level,
                Order = index,
                StartPage = section.StartPage,
                EndPage = section.EndPage,
                Source = section.Source,
                ParentId = section.Level > 1 ? currentTop?.Id : null
            };
            var correction = corrections.FirstOrDefault(c => c.StartPage == chapter.StartPage && c.Level == chapter.Level);
            if (correction is not null)
            {
                chapter.Title = correction.Title;
                chapter.Source = ChapterSource.User;
            }
            if (section.Level == 1) currentTop = chapter;
            chapters.Add(chapter);
        }
        db.Chapters.AddRange(chapters);
        if (chapters.Count == 0)
            warnings.Add(Warning(book.Id, null, "no_structure_detected", WarningSeverity.Info,
                "No reliable chapter structure was found. You can add chapter titles manually."));
        else if (sections[0].Source == ChapterSource.HeadingHeuristic)
            warnings.Add(Warning(book.Id, null, "structure_estimated", WarningSeverity.Info,
                "Chapters were estimated from heading styles. Please review and correct titles if needed."));

        await Stage(job, book, ProcessingStage.ProcessingTablesAndFigures, 62, ct);
        var figurePages = pageEntities.Count(p => p.ImageCount > 0 && !p.IsScanned);
        if (figurePages > 0)
            warnings.Add(Warning(book.Id, null, "figures_not_interpreted", WarningSeverity.Info,
                $"{figurePages} page(s) contain figures or diagrams. Their visual content is not interpreted; only surrounding text is searchable."));
        var tablePages = pageEntities.Where(p => p.HasTableLikeLayout).Select(p => p.PageNumber).ToList();
        if (tablePages.Count > 0)
            warnings.Add(Warning(book.Id, null, "tables_flattened", WarningSeverity.Info,
                $"Tables were detected on {tablePages.Count} page(s); their text is kept but row/column structure may be lost."));

        await Stage(job, book, ProcessingStage.PreparingSearch, 70, ct);
        var chunks = TextChunker.Chunk(
            pageEntities.Select(p => new PageText(p.PageNumber, p.Text, p.ExtractionMethod)).ToList(),
            chapters.Select(c => new ChapterRange(c.Id, c.Title, c.StartPage, c.EndPage)).ToList());

        var chunkEntities = chunks.Select(c => new DocumentChunk
        {
            BookId = book.Id,
            ChapterId = c.ChapterId,
            Ordinal = c.Ordinal,
            PageStart = c.PageStart,
            PageEnd = c.PageEnd,
            SectionTitle = c.SectionTitle,
            Text = c.Text,
            TokenEstimate = TextChunker.EstimateTokens(c.Text),
            ExtractionMethod = c.Method,
            ContentHash = c.ContentHash
        }).ToList();
        db.DocumentChunks.AddRange(chunkEntities);
        db.DocumentWarnings.AddRange(warnings);
        await db.SaveChangesAsync(ct);

        await EmbedChunksAsync(job, book, chunkEntities, vectorByHash, warnings, ct);

        if (chunkEntities.Count == 0)
            db.DocumentWarnings.Add(Warning(book.Id, null, "no_text_extracted", WarningSeverity.Error,
                "No readable text could be extracted, so the tutor and search cannot use this book yet."));

        var hasProblems = warnings.Any(w => w.Severity >= WarningSeverity.Warning) || chunkEntities.Count == 0;
        book.Status = hasProblems ? BookStatus.ReadyWithWarnings : BookStatus.Ready;
        book.ContentVersion++;
        job.Status = JobStatus.Succeeded;
        job.CompletedAt = clock.GetUtcNow().UtcDateTime;
        job.ErrorMessage = null;
        db.Notifications.Add(new Notification
        {
            Id = Ids.New(), UserId = book.UserId, Type = "book_ready", EntityId = book.Id.ToString(),
            Data = book.Title, CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await Stage(job, book, ProcessingStage.Completed, 100, ct);
        logger.LogInformation("Book {BookId} processed: {Pages} pages, {Chunks} chunks, {Chapters} chapters", book.Id, book.PageCount, chunkEntities.Count, chapters.Count);
    }

    private async Task EmbedChunksAsync(DocumentProcessingJob job, Book book, List<DocumentChunk> chunks,
        Dictionary<string, byte[]> reusable, List<DocumentWarning> warnings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = new List<DocumentChunk>();
        foreach (var chunk in chunks)
        {
            if (reusable.TryGetValue(chunk.ContentHash, out var vector))
            {
                db.ChunkEmbeddings.Add(new ChunkEmbedding
                {
                    ChunkId = chunk.Id, BookId = book.Id, Model = embedder.ModelName, Dimensions = vector.Length / 4,
                    Vector = vector, ContentHash = chunk.ContentHash, CreatedAt = now
                });
            }
            else
            {
                pending.Add(chunk);
            }
        }

        var batchSize = Math.Max(1, Math.Min(embedder.MaxBatchSize, _options.EmbeddingBatchSize));
        for (var i = 0; i < pending.Count; i += batchSize)
        {
            var batch = pending.Skip(i).Take(batchSize).ToList();
            float[][] vectors;
            try
            {
                vectors = await embedder.EmbedAsync(batch.Select(c => c.Text).ToList(), EmbeddingPurpose.Document, ct);
            }
            catch (AiUnavailableException) when (job.Attempt < job.MaxAttempts)
            {
                throw; // transient provider failure: let the worker retry the job with back-off
            }
            catch (AiUnavailableException ex)
            {
                logger.LogWarning("Embedding provider unavailable for book {BookId}: {Code}", book.Id, ex.Code);
                db.DocumentWarnings.Add(Warning(book.Id, null, "embeddings_unavailable", WarningSeverity.Warning,
                    "Semantic search vectors could not be generated; keyword search still works. Reprocess later to enable semantic search."));
                warnings.Add(new DocumentWarning { Severity = WarningSeverity.Warning });
                return;
            }

            for (var j = 0; j < batch.Count; j++)
            {
                db.ChunkEmbeddings.Add(new ChunkEmbedding
                {
                    ChunkId = batch[j].Id, BookId = book.Id, Model = embedder.ModelName, Dimensions = vectors[j].Length,
                    Vector = VectorCodec.ToBytes(vectors[j]), ContentHash = batch[j].ContentHash, CreatedAt = now
                });
            }
            await db.SaveChangesAsync(ct);
            await Stage(job, book, ProcessingStage.PreparingSearch, 70 + 28 * Math.Min(pending.Count, i + batchSize) / Math.Max(1, pending.Count), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ClearDerivedContentAsync(Guid bookId, CancellationToken ct)
    {
        await db.ChunkEmbeddings.Where(e => e.BookId == bookId).ExecuteDeleteAsync(ct);
        await db.DocumentChunks.Where(c => c.BookId == bookId).ExecuteDeleteAsync(ct);
        await db.DocumentPages.Where(p => p.BookId == bookId).ExecuteDeleteAsync(ct);
        await db.DocumentWarnings.Where(w => w.BookId == bookId).ExecuteDeleteAsync(ct);
        // Chapter progress rows cascade with their chapters.
        await db.Chapters.Where(c => c.BookId == bookId && c.ParentId != null).ExecuteDeleteAsync(ct);
        await db.Chapters.Where(c => c.BookId == bookId).ExecuteDeleteAsync(ct);
    }

    private async Task Stage(DocumentProcessingJob job, Book book, ProcessingStage stage, int progress, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        job.Stage = stage;
        job.Progress = Math.Clamp(progress, 0, 100);
        job.HeartbeatAt = now;
        book.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await notifier.SendToUserAsync(book.UserId, "processingUpdated", BookMappings.ToStatus(book, job), ct);
    }

    private DocumentWarning Warning(Guid bookId, int? page, string code, WarningSeverity severity, string message) => new()
    {
        BookId = bookId, PageNumber = page, Code = code, Severity = severity, Message = message,
        CreatedAt = clock.GetUtcNow().UtcDateTime
    };

    /// <summary>Tesseract language packs by subject content language. English is added for formulas and Latin terms.</summary>
    public static string OcrLanguages(string? contentLanguage) => contentLanguage switch
    {
        "en" => "eng",
        "fr" => "fra+eng",
        _ => "ara+eng"
    };
}

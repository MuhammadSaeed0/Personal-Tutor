using System.Text.Json;
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
    public int MaxFileSizeMb { get; set; } = 250;
    public int MaxPages { get; set; } = 2000;
    public int MaxImagesPerSource { get; set; } = 100;
    public int MaxImageMb { get; set; } = 25;
    public int MaxVideoMinutes { get; set; } = 240;

    /// <summary>A page with fewer extracted letters than this is treated as scanned/image-only.</summary>
    public int ScannedLetterThreshold { get; set; } = 25;
    public double OcrLowConfidence { get; set; } = 60;

    /// <summary>Scanned pages recognised at the same time.</summary>
    public int OcrParallelism { get; set; } = 2;
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
    IVideoTranscriptSource videos,
    TranscriptStructurer structurer,
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

        // Every kind of source becomes numbered pages of text; everything after this is shared.
        var content = book.Kind switch
        {
            SourceKind.Images => await LoadImagesAsync(job, book, warnings, ct),
            SourceKind.YouTube => await LoadVideoAsync(job, book, warnings, ct),
            _ => await LoadPdfAsync(job, book, warnings, ct)
        };
        book.PageCount = content.Pages.Count;

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

        var pageEntities = content.Pages.Select(p => new DocumentPage
        {
            BookId = book.Id,
            PageNumber = p.Number,
            Text = p.Text,
            CharCount = p.Text.Length,
            IsScanned = p.IsScanned,
            ExtractionMethod = p.Method,
            ImageCount = p.ImageCount,
            HasMathNotation = TextCleaner.LooksMathematical(p.Text),
            HasTableLikeLayout = p.IsTabular,
            StartSeconds = p.StartSeconds,
            EndSeconds = p.EndSeconds
        }).ToList();
        db.DocumentPages.AddRange(pageEntities);

        var allText = string.Join('\n', pageEntities.Take(40).Select(p => p.Text));
        book.DetectedLanguage = LanguageDetector.Detect(allText);

        await Stage(job, book, ProcessingStage.DetectingStructure, 55, ct);
        var sections = content.Sections;
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
        else if (sections[0].Source == ChapterSource.Topics)
            warnings.Add(Warning(book.Id, null, "topics_generated", WarningSeverity.Info,
                "Sections were named by AI from the video's topics. Please review the titles if needed."));

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

    private sealed record SourcePage(int Number, string Text, ExtractionMethod Method, bool IsScanned, int ImageCount, bool IsTabular,
        int? StartSeconds = null, int? EndSeconds = null);

    private sealed record SourceContent(List<SourcePage> Pages, List<DetectedSection> Sections);

    private async Task<SourceContent> LoadPdfAsync(DocumentProcessingJob job, Book book, List<DocumentWarning> warnings, CancellationToken ct)
    {
        var path = await storage.GetLocalPathAsync(book.StorageKey, ct);
        var validation = extractor.Validate(path);
        if (!validation.IsValid) throw new PermanentProcessingException(validation.ErrorCode ?? "pdf_unreadable");
        if (validation.PageCount > _options.MaxPages) throw new PermanentProcessingException("pdf_too_many_pages");

        await Stage(job, book, ProcessingStage.ExtractingText, 10, ct);
        var document = await Task.Run(() => extractor.Extract(path, ct), ct);
        book.PageCount = document.PageCount;

        // Embedded text first, OCR for scanned pages. A scan carrying another tool's invisible OCR layer is still a scan:
        // that layer is re-read from the image and kept only as a fallback.
        bool IsScanned(ExtractedPage p) => p.LetterCount < _options.ScannedLetterThreshold || p.HasInvisibleTextLayer;
        var languages = OcrLanguages(book.Subject?.ContentLanguage);
        var scanned = document.Pages.Where(IsScanned).Select(p => p.Number).ToList();
        var ocrText = await RecognizeAsync(job, book, scanned, n => ocr.RecognizePageAsync(path, n, languages, ct), warnings, ct);

        var pages = document.Pages.Select(p =>
        {
            var hasOcr = ocrText.TryGetValue(p.Number, out var recognised);
            var text = hasOcr ? recognised! : p.Text;
            return new SourcePage(p.Number, text,
                hasOcr ? ExtractionMethod.Ocr : text.Length > 0 ? ExtractionMethod.EmbeddedText : ExtractionMethod.None,
                IsScanned(p), p.ImageCount, TextCleaner.LooksTabular(p.Lines.Select(l => l.Text)));
        }).ToList();

        var sections = StructureDetector.Detect(document);
        if (sections.Count == 0 && ocrText.Count > 0) sections = StructureDetector.FromRecognisedText(ocrText, document.PageCount);
        return new SourceContent(pages, sections);
    }

    private async Task<SourceContent> LoadImagesAsync(DocumentProcessingJob job, Book book, List<DocumentWarning> warnings, CancellationToken ct)
    {
        await Stage(job, book, ProcessingStage.ExtractingText, 10, ct);
        var numbers = Enumerable.Range(1, book.PageCount).ToList();
        var languages = OcrLanguages(book.Subject?.ContentLanguage);
        var ocrText = await RecognizeAsync(job, book, numbers, async n =>
        {
            await using var stream = await storage.OpenReadAsync(SourceFiles.ImageKey(book, n), ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return await ocr.RecognizeImageAsync(buffer.ToArray(), languages, ct);
        }, warnings, ct);

        var pages = numbers.Select(n =>
        {
            var text = ocrText.GetValueOrDefault(n, string.Empty);
            return new SourcePage(n, text, text.Length > 0 ? ExtractionMethod.Ocr : ExtractionMethod.None, true, 1, false);
        }).ToList();
        return new SourceContent(pages, StructureDetector.FromRecognisedText(ocrText, numbers.Count));
    }

    private async Task<SourceContent> LoadVideoAsync(DocumentProcessingJob job, Book book, List<DocumentWarning> warnings, CancellationToken ct)
    {
        await Stage(job, book, ProcessingStage.ExtractingText, 10, ct);
        var transcript = await ReadJsonAsync<VideoTranscript>(book.StorageKey, ct);
        if (transcript is null)
        {
            transcript = await videos.GetTranscriptAsync(book.ExternalId!, book.Subject?.ContentLanguage ?? "ar", ct)
                         ?? throw new PermanentProcessingException("video_no_captions");
            await WriteJsonAsync(book.StorageKey, transcript, ct);
        }
        if (transcript.IsAutoGenerated)
            warnings.Add(Warning(book.Id, null, "transcript_auto_generated", WarningSeverity.Info,
                "The captions were generated automatically by YouTube and may contain recognition errors."));

        var windows = TranscriptSegmenter.Segment(transcript.Captions);
        var cacheKey = SourceFiles.TranscriptCacheKey(book);
        StructuredTranscript? structured = null;
        if (windows.Count > 0)
        {
            await Stage(job, book, ProcessingStage.CleaningTranscript, 30, ct);
            if (structurer.IsAvailable)
            {
                // Saved after every request, so an interrupted run continues where it stopped.
                var known = await ReadJsonAsync<StructuredTranscript>(cacheKey, ct);
                structured = await structurer.StructureAsync(windows, known, async progress =>
                {
                    await WriteJsonAsync(cacheKey, progress, ct);
                    await Stage(job, book, ProcessingStage.CleaningTranscript, 30 + 20 * progress.CleanText.Count / windows.Count, ct);
                }, ct);
                await WriteJsonAsync(cacheKey, structured, ct);
            }
            if (structured is not { Complete: true })
                warnings.Add(Warning(book.Id, null, "transcript_not_cleaned", WarningSeverity.Warning,
                    "Part of the transcript could not be tidied by AI and is shown as raw captions. Reprocess later to finish it."));
        }

        var pages = windows.Select(w => new SourcePage(w.Number, structured?.CleanText.GetValueOrDefault(w.Number) ?? w.Text,
            ExtractionMethod.Transcript, false, 0, false, w.StartSeconds, w.EndSeconds)).ToList();
        var sections = StructureDetector.FromTopics((structured?.Topics ?? []).Select(t => (t.Title, t.StartWindow)).ToList(), windows.Count);
        return new SourceContent(pages, sections);
    }

    /// <summary>
    /// Reads image pages with the OCR engine. Pages read by an earlier (possibly interrupted) run are reused, each
    /// new result is saved straight away, and once the AI usage limit is hit no further pages are attempted.
    /// </summary>
    private async Task<Dictionary<int, string>> RecognizeAsync(DocumentProcessingJob job, Book book, List<int> pageNumbers,
        Func<int, Task<OcrResult>> recognize, List<DocumentWarning> warnings, CancellationToken ct)
    {
        var ocrText = new Dictionary<int, string>();
        if (pageNumbers.Count == 0) return ocrText;

        await Stage(job, book, ProcessingStage.RecognizingScannedPages, 30, ct);
        if (!ocr.IsAvailable)
        {
            foreach (var number in pageNumbers)
                warnings.Add(Warning(book.Id, number, "scanned_page_no_ocr", WarningSeverity.Warning,
                    "This page is an image and OCR is not available on the server."));
            return ocrText;
        }

        var previous = await db.RecognizedPages
            .Where(p => p.BookId == book.Id)
            .ToDictionaryAsync(p => p.PageNumber, p => (p.Text, p.Confidence), ct);
        var legacy = await db.DocumentPages
            .Where(p => p.BookId == book.Id && p.ExtractionMethod == ExtractionMethod.Ocr)
            .Select(p => new { p.PageNumber, p.Text })
            .ToListAsync(ct);
        foreach (var page in legacy) previous.TryAdd(page.PageNumber, (page.Text, null));

        var done = 0;
        var limitReached = false;
        foreach (var batch in pageNumbers.Chunk(Math.Max(1, _options.OcrParallelism)))
        {
            var results = await Task.WhenAll(batch.Select(async number =>
            {
                if (previous.TryGetValue(number, out var known)) return (number, Result: new OcrResult(true, known.Text, known.Confidence, null), Reused: true);
                if (limitReached) return (number, Result: new OcrResult(false, string.Empty, null, "ocr_rate_limited"), Reused: false);
                return (number, Result: await recognize(number), Reused: false);
            }));

            foreach (var (number, result, reused) in results)
            {
                if (result.Success)
                {
                    ocrText[number] = reused ? result.Text : TextCleaner.CleanExtracted(result.Text);
                    if (!reused)
                    {
                        db.RecognizedPages.Add(new RecognizedPage
                        {
                            BookId = book.Id, PageNumber = number, Text = ocrText[number],
                            Confidence = result.MeanConfidence, CreatedAt = clock.GetUtcNow().UtcDateTime
                        });
                    }
                    if (result.MeanConfidence < _options.OcrLowConfidence)
                        warnings.Add(Warning(book.Id, number, "ocr_low_confidence", WarningSeverity.Warning,
                            "Text recognised on this page may contain recognition errors."));
                }
                else if (result.ErrorCode == "ocr_rate_limited")
                {
                    limitReached = true;
                    warnings.Add(Warning(book.Id, number, "ocr_rate_limited", WarningSeverity.Warning,
                        "The AI usage limit was reached before this page could be read. Reprocess later to read the remaining pages."));
                }
                else if (result.ErrorCode != "ocr_no_text")
                {
                    warnings.Add(Warning(book.Id, number, "ocr_failed", WarningSeverity.Warning, "Text on this page could not be recognised."));
                }
            }

            done += batch.Length;
            await Stage(job, book, ProcessingStage.RecognizingScannedPages, 30 + 20 * done / pageNumbers.Count, ct);
        }
        return ocrText;
    }

    private async Task<T?> ReadJsonAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (string.IsNullOrEmpty(key) || !await storage.ExistsAsync(key, ct)) return null;
        await using var stream = await storage.OpenReadAsync(key, ct);
        try { return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct); }
        catch (JsonException) { return null; }
    }

    private async Task WriteJsonAsync<T>(string key, T value, CancellationToken ct)
    {
        using var buffer = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(value));
        await storage.SaveAsync(key, buffer, ct);
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

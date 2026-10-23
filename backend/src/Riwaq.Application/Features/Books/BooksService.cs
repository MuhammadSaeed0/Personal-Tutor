using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Ingestion;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Books;

public sealed class BooksService(
    IAppDbContext db,
    IFileStorage storage,
    IPdfExtractor extractor,
    IProcessingSignal signal,
    IOptions<ProcessingOptions> options,
    TimeProvider clock)
{
    private readonly ProcessingOptions _options = options.Value;

    public long MaxUploadBytes => _options.MaxFileSizeMb * 1024L * 1024L;

    public async Task<BookDto> UploadAsync(Guid userId, Stream content, string fileName, UploadBookRequest request, CancellationToken ct)
    {
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("file_not_pdf", "Only PDF files can be uploaded.");
        if (!await db.Subjects.AnyAsync(s => s.Id == request.SubjectId, ct))
            throw new ValidationException("invalid_subject", "Unknown subject.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"riwaq-upload-{Guid.NewGuid():N}.pdf");
        try
        {
            // Copy to a temp file while hashing, enforcing the size limit as bytes arrive.
            string sha256;
            long size;
            await using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                size = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    size += read;
                    if (size > MaxUploadBytes)
                        throw new ValidationException("file_too_large", $"The file exceeds the {_options.MaxFileSizeMb} MB limit.");
                    hasher.AppendData(buffer, 0, read);
                    await temp.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                sha256 = Convert.ToHexString(hasher.GetHashAndReset());
            }
            if (size == 0) throw new ValidationException("file_empty", "The file is empty.");

            var duplicate = await db.Books.Where(b => b.UserId == userId && b.Sha256 == sha256).Select(b => new { b.Id, b.Title }).FirstOrDefaultAsync(ct);
            if (duplicate is not null)
            {
                var conflict = new ConflictException("duplicate_book", "This PDF has already been uploaded.");
                conflict.Extensions["bookId"] = duplicate.Id;
                conflict.Extensions["title"] = duplicate.Title;
                throw conflict;
            }

            var validation = extractor.Validate(tempPath);
            if (!validation.IsValid) throw new ValidationException(validation.ErrorCode ?? "pdf_unreadable", "The PDF could not be read.");
            if (validation.PageCount > _options.MaxPages)
                throw new ValidationException("pdf_too_many_pages", $"The PDF has more than {_options.MaxPages} pages.");

            var now = clock.GetUtcNow().UtcDateTime;
            var bookId = Ids.New();
            var book = new Book
            {
                Id = bookId,
                UserId = userId,
                SubjectId = request.SubjectId,
                Title = CleanTitle(request.Title) ?? CleanTitle(Path.GetFileNameWithoutExtension(fileName)) ?? "Untitled",
                AcademicYear = request.AcademicYear?.Trim(),
                Semester = request.Semester?.Trim(),
                Collection = request.Collection?.Trim(),
                Tags = NormalizeTags(request.Tags),
                Status = BookStatus.Uploaded,
                PageCount = validation.PageCount,
                StorageKey = $"books/{userId:N}/{bookId:N}.pdf",
                OriginalFileName = Path.GetFileName(fileName).Length > 200 ? Path.GetFileName(fileName)[..200] : Path.GetFileName(fileName),
                FileSizeBytes = size,
                Sha256 = sha256,
                CreatedAt = now,
                UpdatedAt = now
            };

            await using (var file = File.OpenRead(tempPath))
            {
                await storage.SaveAsync(book.StorageKey, file, ct);
            }

            var job = NewJob(book, now);
            db.Books.Add(book);
            db.ProcessingJobs.Add(job);
            await db.SaveChangesAsync(ct);
            signal.Notify();
            return BookMappings.ToDto(book, job, null);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    public async Task<IReadOnlyList<BookDto>> ListAsync(Guid userId, int? subjectId, CancellationToken ct)
    {
        var query = db.Books.AsNoTracking().Where(b => b.UserId == userId);
        if (subjectId is not null) query = query.Where(b => b.SubjectId == subjectId);
        var books = await query.OrderByDescending(b => b.LastOpenedAt ?? b.CreatedAt).ToListAsync(ct);
        var ids = books.Select(b => b.Id).ToList();

        var jobs = await db.ProcessingJobs.AsNoTracking().Where(j => ids.Contains(j.BookId))
            .GroupBy(j => j.BookId).Select(g => g.OrderByDescending(j => j.CreatedAt).First()).ToListAsync(ct);
        var progress = await db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId && ids.Contains(p.BookId)).ToListAsync(ct);

        return books.Select(b => BookMappings.ToDto(b,
            jobs.FirstOrDefault(j => j.BookId == b.Id && j.Status != JobStatus.Succeeded),
            progress.FirstOrDefault(p => p.BookId == b.Id))).ToList();
    }

    public async Task<BookDetailDto> GetAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var book = await GetOwnedAsync(userId, bookId, ct, tracking: false);
        var job = await LatestJobAsync(bookId, ct);
        var progress = await db.ReadingProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, ct);
        var chapters = await GetChaptersAsync(userId, bookId, ct);
        var warnings = await db.DocumentWarnings.AsNoTracking().Where(w => w.BookId == bookId)
            .OrderBy(w => w.PageNumber).Select(w => new WarningDto(w.Id, w.PageNumber, w.Code, w.Message, w.Severity)).ToListAsync(ct);
        var pages = await db.DocumentPages.AsNoTracking().Where(p => p.BookId == bookId).OrderBy(p => p.PageNumber)
            .Select(p => new PageInfoDto(p.PageNumber, p.CharCount, p.ExtractionMethod, p.IsScanned, p.ImageCount, p.HasMathNotation, p.HasTableLikeLayout))
            .ToListAsync(ct);
        return new BookDetailDto(BookMappings.ToDto(book, job?.Status == JobStatus.Succeeded ? null : job, progress), chapters, warnings, pages);
    }

    public async Task<ProcessingStatusDto> GetStatusAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var book = await GetOwnedAsync(userId, bookId, ct, tracking: false);
        return BookMappings.ToStatus(book, await LatestJobAsync(bookId, ct));
    }

    public async Task<IReadOnlyList<ChapterDto>> GetChaptersAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        await EnsureOwnedAsync(userId, bookId, ct);
        var progress = await db.ChapterProgress.AsNoTracking().Where(p => p.UserId == userId && p.BookId == bookId)
            .ToDictionaryAsync(p => p.ChapterId, p => p.Status, ct);
        var chapters = await db.Chapters.AsNoTracking().Where(c => c.BookId == bookId).OrderBy(c => c.Order).ToListAsync(ct);
        return chapters.Select(c => new ChapterDto(c.Id, c.ParentId, c.Title, c.DetectedTitle, c.Level, c.Order, c.StartPage, c.EndPage, c.Source,
            progress.GetValueOrDefault(c.Id, ChapterStatus.NotStarted))).ToList();
    }

    public async Task<PageTextDto> GetPageTextAsync(Guid userId, Guid bookId, int pageNumber, CancellationToken ct)
    {
        await EnsureOwnedAsync(userId, bookId, ct);
        var page = await db.DocumentPages.AsNoTracking().FirstOrDefaultAsync(p => p.BookId == bookId && p.PageNumber == pageNumber, ct)
                   ?? throw new NotFoundException("Page");
        return new PageTextDto(page.PageNumber, page.Text, page.ExtractionMethod, page.IsScanned);
    }

    public async Task<BookDto> UpdateAsync(Guid userId, Guid bookId, UpdateBookRequest request, CancellationToken ct)
    {
        var book = await GetOwnedAsync(userId, bookId, ct);
        if (request.Title is not null) book.Title = CleanTitle(request.Title) ?? throw new ValidationException("invalid_title", "Title is required.");
        if (request.SubjectId is not null)
        {
            if (!await db.Subjects.AnyAsync(s => s.Id == request.SubjectId, ct))
                throw new ValidationException("invalid_subject", "Unknown subject.");
            book.SubjectId = request.SubjectId.Value;
        }
        if (request.AcademicYear is not null) book.AcademicYear = NullIfEmpty(request.AcademicYear);
        if (request.Semester is not null) book.Semester = NullIfEmpty(request.Semester);
        if (request.Collection is not null) book.Collection = NullIfEmpty(request.Collection);
        if (request.Tags is not null) book.Tags = NormalizeTags(request.Tags);
        book.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return BookMappings.ToDto(book, null, null);
    }

    public async Task<ChapterDto> UpdateChapterAsync(Guid userId, Guid bookId, Guid chapterId, UpdateChapterRequest request, CancellationToken ct)
    {
        await EnsureOwnedAsync(userId, bookId, ct);
        var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId && c.BookId == bookId, ct) ?? throw new NotFoundException("Chapter");
        var title = CleanTitle(request.Title) ?? throw new ValidationException("invalid_title", "Title is required.");
        chapter.Title = title;
        chapter.Source = ChapterSource.User;
        await db.SaveChangesAsync(ct);
        return new ChapterDto(chapter.Id, chapter.ParentId, chapter.Title, chapter.DetectedTitle, chapter.Level, chapter.Order, chapter.StartPage, chapter.EndPage, chapter.Source, ChapterStatus.NotStarted);
    }

    public async Task<ProcessingStatusDto> ReprocessAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var book = await GetOwnedAsync(userId, bookId, ct);
        var active = await db.ProcessingJobs.AnyAsync(j => j.BookId == bookId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running), ct);
        if (active) throw new ConflictException("processing_in_progress", "This book is already being processed.");

        var job = NewJob(book, clock.GetUtcNow().UtcDateTime);
        book.Status = BookStatus.Uploaded;
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return BookMappings.ToStatus(book, job);
    }

    public async Task DeleteAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var book = await GetOwnedAsync(userId, bookId, ct);
        // Pending plan tasks that point at the book are removed; history (quizzes, cards, notes) is kept without the link.
        await db.StudyTasks.Where(t => t.UserId == userId && t.BookId == bookId && t.Status == StudyTaskStatus.Pending).ExecuteDeleteAsync(ct);
        db.Books.Remove(book);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(book.StorageKey, ct);
    }

    public async Task<(Stream Stream, string FileName)> OpenFileAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var book = await GetOwnedAsync(userId, bookId, ct, tracking: false);
        return (await storage.OpenReadAsync(book.StorageKey, ct), book.OriginalFileName);
    }

    public async Task MarkOpenedAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        await db.Books.Where(b => b.Id == bookId && b.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.LastOpenedAt, clock.GetUtcNow().UtcDateTime), ct);
    }

    public async Task<Book> GetOwnedAsync(Guid userId, Guid bookId, CancellationToken ct, bool tracking = true)
    {
        var query = tracking ? db.Books : db.Books.AsNoTracking();
        return await query.FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
    }

    private async Task EnsureOwnedAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId && b.UserId == userId, ct)) throw new NotFoundException("Book");
    }

    private Task<DocumentProcessingJob?> LatestJobAsync(Guid bookId, CancellationToken ct) =>
        db.ProcessingJobs.AsNoTracking().Where(j => j.BookId == bookId).OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);

    private static DocumentProcessingJob NewJob(Book book, DateTime now) => new()
    {
        Id = Ids.New(), BookId = book.Id, UserId = book.UserId, Status = JobStatus.Queued, Stage = ProcessingStage.Queued, CreatedAt = now
    };

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var t = title.Trim();
        return t.Length > 300 ? t[..300] : t;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        tags?.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList() ?? [];
}

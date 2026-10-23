using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Reader;

public sealed record ReaderStateDto(int LastPage, IReadOnlyList<int> ReadPages, int ReadPageCount, int TotalSeconds, IReadOnlyList<BookmarkDto> Bookmarks);

public sealed record ProgressUpdateRequest(int Page, int SecondsOnPage);

public sealed record BookmarkDto(Guid Id, int PageNumber, string? Label, DateTime CreatedAt);

public sealed record CreateBookmarkRequest(int PageNumber, string? Label);

public sealed record HighlightRect(double X, double Y, double W, double H);

public sealed record HighlightDto(Guid Id, int PageNumber, string Text, string Color, IReadOnlyList<HighlightRect> Rects, string? Annotation, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateHighlightRequest(int PageNumber, string Text, string? Color, IReadOnlyList<HighlightRect> Rects, string? Annotation);

public sealed record UpdateHighlightRequest(string? Color, string? Annotation);

public sealed record ChapterStatusRequest(ChapterStatus Status);

public sealed class ReaderService(IAppDbContext db, TimeProvider clock)
{
    /// <summary>A page counts as read after this many seconds of attention.</summary>
    public const int SecondsToCountPage = 20;
    private const double ChapterCompleteFraction = 0.8;
    private static readonly string[] Colors = ["yellow", "green", "blue", "pink", "purple"];

    public async Task<ReaderStateDto> GetStateAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        await EnsureOwnedAsync(userId, bookId, ct);
        var progress = await db.ReadingProgress.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, ct);
        var bookmarks = await db.Bookmarks.AsNoTracking().Where(b => b.UserId == userId && b.BookId == bookId).OrderBy(b => b.PageNumber)
            .Select(b => new BookmarkDto(b.Id, b.PageNumber, b.Label, b.CreatedAt)).ToListAsync(ct);
        return new ReaderStateDto(progress?.LastPage ?? 1, progress is null ? [] : PagesFromBits(progress.ReadPages),
            progress?.ReadPageCount ?? 0, progress?.TotalSeconds ?? 0, bookmarks);
    }

    /// <summary>
    /// Reading heartbeat from the reader (sent every ~30 s and on page change). Updates the resume position,
    /// marks pages as read after real attention, accumulates reading time into a study session and
    /// updates chapter progress.
    /// </summary>
    public async Task<ReaderStateDto> UpdateProgressAsync(Guid userId, Guid bookId, ProgressUpdateRequest request, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
        if (request.Page < 1 || (book.PageCount > 0 && request.Page > book.PageCount)) throw new ValidationException("invalid_page", "Invalid page.");
        var seconds = Math.Clamp(request.SecondsOnPage, 0, 120);
        var now = clock.GetUtcNow().UtcDateTime;

        var progress = await db.ReadingProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, ct);
        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId, BookId = bookId, ReadPages = new byte[(Math.Max(book.PageCount, request.Page) + 7) / 8] };
            db.ReadingProgress.Add(progress);
        }
        progress.LastPage = request.Page;
        progress.TotalSeconds += seconds;
        progress.UpdatedAt = now;

        var newlyRead = false;
        if (seconds >= SecondsToCountPage && !IsSet(progress.ReadPages, request.Page))
        {
            progress.ReadPages = SetBit(progress.ReadPages, request.Page);
            progress.ReadPageCount++;
            newlyRead = true;
        }

        if (seconds > 0) await AddReadingTimeAsync(userId, book, seconds, now, ct);
        if (newlyRead) await UpdateChapterProgressAsync(userId, bookId, progress.ReadPages, request.Page, now, ct);

        await db.SaveChangesAsync(ct);
        return new ReaderStateDto(progress.LastPage, PagesFromBits(progress.ReadPages), progress.ReadPageCount, progress.TotalSeconds, []);
    }

    public async Task SetChapterStatusAsync(Guid userId, Guid bookId, Guid chapterId, ChapterStatusRequest request, CancellationToken ct)
    {
        await EnsureOwnedAsync(userId, bookId, ct);
        if (!await db.Chapters.AnyAsync(c => c.Id == chapterId && c.BookId == bookId, ct)) throw new NotFoundException("Chapter");
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.ChapterProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (row is null)
        {
            row = new ChapterProgress { UserId = userId, ChapterId = chapterId, BookId = bookId };
            db.ChapterProgress.Add(row);
        }
        row.Status = request.Status;
        row.UpdatedAt = now;
        row.CompletedAt = request.Status is ChapterStatus.Completed or ChapterStatus.AlreadyKnown ? now : null;
        await db.SaveChangesAsync(ct);
    }

    // ---- Bookmarks ---------------------------------------------------------------------------------------

    public async Task<BookmarkDto> AddBookmarkAsync(Guid userId, Guid bookId, CreateBookmarkRequest request, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
        if (request.PageNumber < 1 || request.PageNumber > Math.Max(1, book.PageCount)) throw new ValidationException("invalid_page", "Invalid page.");
        var existing = await db.Bookmarks.FirstOrDefaultAsync(b => b.UserId == userId && b.BookId == bookId && b.PageNumber == request.PageNumber, ct);
        if (existing is not null)
        {
            existing.Label = Trim(request.Label, 200);
            await db.SaveChangesAsync(ct);
            return new BookmarkDto(existing.Id, existing.PageNumber, existing.Label, existing.CreatedAt);
        }
        var bookmark = new Bookmark { Id = Ids.New(), UserId = userId, BookId = bookId, PageNumber = request.PageNumber, Label = Trim(request.Label, 200), CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.Bookmarks.Add(bookmark);
        await db.SaveChangesAsync(ct);
        return new BookmarkDto(bookmark.Id, bookmark.PageNumber, bookmark.Label, bookmark.CreatedAt);
    }

    public async Task DeleteBookmarkAsync(Guid userId, Guid bookId, Guid bookmarkId, CancellationToken ct)
    {
        var deleted = await db.Bookmarks.Where(b => b.Id == bookmarkId && b.BookId == bookId && b.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Bookmark");
    }

    // ---- Highlights & annotations --------------------------------------------------------------------

    public async Task<IReadOnlyList<HighlightDto>> GetHighlightsAsync(Guid userId, Guid bookId, int? page, CancellationToken ct)
    {
        await EnsureOwnedAsync(userId, bookId, ct);
        var query = db.Highlights.AsNoTracking().Where(h => h.UserId == userId && h.BookId == bookId);
        if (page is not null) query = query.Where(h => h.PageNumber == page);
        var items = await query.OrderBy(h => h.PageNumber).ThenBy(h => h.CreatedAt).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<HighlightDto> AddHighlightAsync(Guid userId, Guid bookId, CreateHighlightRequest request, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
        if (request.PageNumber < 1 || request.PageNumber > Math.Max(1, book.PageCount)) throw new ValidationException("invalid_page", "Invalid page.");
        var text = request.Text?.Trim() ?? "";
        if (text.Length is 0 or > 4000) throw new ValidationException("invalid_highlight", "Highlighted text must be 1–4000 characters.");
        if (request.Rects is null || request.Rects.Count is 0 or > 200 || request.Rects.Any(r => r.X is < 0 or > 1 || r.Y is < 0 or > 1 || r.W is <= 0 or > 1 || r.H is <= 0 or > 1))
            throw new ValidationException("invalid_highlight", "Invalid highlight position.");

        var now = clock.GetUtcNow().UtcDateTime;
        var highlight = new Highlight
        {
            Id = Ids.New(), UserId = userId, BookId = bookId, PageNumber = request.PageNumber, Text = text,
            Color = Colors.Contains(request.Color) ? request.Color! : "yellow",
            RectsJson = JsonSerializer.Serialize(request.Rects), Annotation = Trim(request.Annotation, 4000), CreatedAt = now, UpdatedAt = now
        };
        db.Highlights.Add(highlight);
        await db.SaveChangesAsync(ct);
        return ToDto(highlight);
    }

    public async Task<HighlightDto> UpdateHighlightAsync(Guid userId, Guid bookId, Guid highlightId, UpdateHighlightRequest request, CancellationToken ct)
    {
        var highlight = await db.Highlights.FirstOrDefaultAsync(h => h.Id == highlightId && h.BookId == bookId && h.UserId == userId, ct) ?? throw new NotFoundException("Highlight");
        if (request.Color is not null && Colors.Contains(request.Color)) highlight.Color = request.Color;
        if (request.Annotation is not null) highlight.Annotation = Trim(request.Annotation, 4000);
        highlight.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(highlight);
    }

    public async Task DeleteHighlightAsync(Guid userId, Guid bookId, Guid highlightId, CancellationToken ct)
    {
        var deleted = await db.Highlights.Where(h => h.Id == highlightId && h.BookId == bookId && h.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Highlight");
    }

    // ---- Internals ------------------------------------------------------------------------------------

    private async Task AddReadingTimeAsync(Guid userId, Book book, int seconds, DateTime now, CancellationToken ct)
    {
        // Consecutive heartbeats extend one reading session; a gap of 10+ minutes starts a new one.
        var cutoff = now.AddMinutes(-10);
        var session = await db.StudySessions.FirstOrDefaultAsync(s => s.UserId == userId && s.BookId == book.Id && s.Type == StudySessionType.Reading && s.EndedAt >= cutoff, ct);
        if (session is null)
        {
            session = new StudySession
            {
                Id = Ids.New(), UserId = userId, Type = StudySessionType.Reading, SubjectId = book.SubjectId, BookId = book.Id,
                StartedAt = now.AddSeconds(-seconds), EndedAt = now
            };
            db.StudySessions.Add(session);
        }
        session.DurationSeconds += seconds;
        session.EndedAt = now;
    }

    private async Task UpdateChapterProgressAsync(Guid userId, Guid bookId, byte[] readPages, int page, DateTime now, CancellationToken ct)
    {
        var chapters = await db.Chapters.AsNoTracking().Where(c => c.BookId == bookId && c.StartPage <= page && c.EndPage >= page).ToListAsync(ct);
        foreach (var chapter in chapters)
        {
            var row = await db.ChapterProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapter.Id, ct);
            if (row?.Status is ChapterStatus.Completed or ChapterStatus.AlreadyKnown) continue;
            if (row is null)
            {
                row = new ChapterProgress { UserId = userId, ChapterId = chapter.Id, BookId = bookId };
                db.ChapterProgress.Add(row);
            }
            var total = chapter.EndPage - chapter.StartPage + 1;
            var read = Enumerable.Range(chapter.StartPage, total).Count(p => IsSet(readPages, p));
            row.Status = read >= Math.Ceiling(total * ChapterCompleteFraction) ? ChapterStatus.Completed : ChapterStatus.InProgress;
            row.CompletedAt = row.Status == ChapterStatus.Completed ? now : null;
            row.UpdatedAt = now;
        }
    }

    public static bool IsSet(byte[] bits, int page) => (page - 1) / 8 < bits.Length && (bits[(page - 1) / 8] & (1 << ((page - 1) % 8))) != 0;

    private static byte[] SetBit(byte[] bits, int page)
    {
        var index = (page - 1) / 8;
        var copy = bits.Length > index ? (byte[])bits.Clone() : bits.Concat(new byte[index - bits.Length + 1]).ToArray();
        copy[index] |= (byte)(1 << ((page - 1) % 8));
        return copy;
    }

    public static List<int> PagesFromBits(byte[] bits)
    {
        var pages = new List<int>();
        for (var i = 0; i < bits.Length * 8; i++)
            if ((bits[i / 8] & (1 << (i % 8))) != 0) pages.Add(i + 1);
        return pages;
    }

    private async Task EnsureOwnedAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        if (!await db.Books.AnyAsync(b => b.Id == bookId && b.UserId == userId, ct)) throw new NotFoundException("Book");
    }

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    private static HighlightDto ToDto(Highlight h) => new(h.Id, h.PageNumber, h.Text, h.Color,
        JsonSerializer.Deserialize<List<HighlightRect>>(h.RectsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [], h.Annotation, h.CreatedAt, h.UpdatedAt);
}

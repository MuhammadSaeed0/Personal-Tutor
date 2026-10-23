using System.Text;
using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Notes;

public sealed record NoteDto(Guid Id, int? SubjectId, Guid? BookId, Guid? ChapterId, int? PageNumber, string Title, string Content,
    IReadOnlyList<string> Tags, bool IsPinned, ContentSourceType SourceType, IReadOnlyList<SourceReference> Sources, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SaveNoteRequest(int? SubjectId, Guid? BookId, Guid? ChapterId, int? PageNumber, string? Title, string Content,
    IReadOnlyList<string>? Tags, bool? IsPinned, ContentSourceType SourceType = ContentSourceType.Manual);

public sealed record NoteFromMessageRequest(Guid ConversationId, Guid MessageId, string? Title);

public sealed class NotesService(IAppDbContext db, TimeProvider clock)
{
    public async Task<PagedResult<NoteDto>> ListAsync(Guid userId, int? subjectId, Guid? bookId, string? tag, string? search, bool? pinned, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Notes.AsNoTracking().Where(n => n.UserId == userId);
        if (subjectId is not null) query = query.Where(n => n.SubjectId == subjectId);
        if (bookId is not null) query = query.Where(n => n.BookId == bookId);
        if (pinned is not null) query = query.Where(n => n.IsPinned == pinned);
        if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(n => n.Tags.Contains(tag));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(n => n.Title.Contains(search) || n.Content.Contains(search));
        var paged = await query.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt).ToPagedAsync(page, pageSize, ct);
        return new PagedResult<NoteDto>(paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<NoteDto> GetAsync(Guid userId, Guid id, CancellationToken ct) => ToDto(await GetOwnedAsync(userId, id, ct));

    public async Task<NoteDto> CreateAsync(Guid userId, SaveNoteRequest request, CancellationToken ct)
    {
        var note = new Note { Id = Ids.New(), UserId = userId, CreatedAt = clock.GetUtcNow().UtcDateTime, SourceType = request.SourceType };
        await ApplyAsync(userId, note, request, ct);
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return ToDto(note);
    }

    public async Task<NoteDto> UpdateAsync(Guid userId, Guid id, SaveNoteRequest request, CancellationToken ct)
    {
        var note = await GetOwnedAsync(userId, id, ct);
        await ApplyAsync(userId, note, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(note);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var deleted = await db.Notes.Where(n => n.Id == id && n.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Note");
    }

    /// <summary>Saves a tutor answer into the notebook, keeping its verified textbook citations.</summary>
    public async Task<NoteDto> CreateFromMessageAsync(Guid userId, NoteFromMessageRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ConversationId && c.UserId == userId, ct)
                           ?? throw new NotFoundException("Conversation");
        var message = await db.ConversationMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MessageId && m.ConversationId == conversation.Id && m.Role == MessageRole.Assistant, ct)
            ?? throw new NotFoundException("Message");
        var question = await db.ConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && m.Role == MessageRole.User && m.CreatedAt <= message.CreatedAt)
            .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) title = question?.Content is { Length: > 0 } q ? q : conversation.Title;
        var note = new Note
        {
            Id = Ids.New(), UserId = userId, SubjectId = conversation.SubjectId, BookId = conversation.BookId,
            PageNumber = question?.ContextPage ?? message.Citations.FirstOrDefault()?.PageStart,
            Title = Truncate(string.IsNullOrEmpty(title) ? "Tutor explanation" : title, 300), Content = message.Content,
            SourceType = ContentSourceType.AiExplanation,
            Sources = message.Citations.Select(c => new SourceReference
            {
                Marker = c.Marker, BookId = c.BookId, BookTitle = c.BookTitle, ChunkId = c.ChunkId, ChapterTitle = c.ChapterTitle,
                PageStart = c.PageStart, PageEnd = c.PageEnd, Quote = c.Quote
            }).ToList(),
            CreatedAt = now, UpdatedAt = now
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return ToDto(note);
    }

    public async Task<(string FileName, string Markdown)> ExportAsync(Guid userId, int? subjectId, Guid? bookId, CancellationToken ct)
    {
        var query = db.Notes.AsNoTracking().Where(n => n.UserId == userId);
        if (subjectId is not null) query = query.Where(n => n.SubjectId == subjectId);
        if (bookId is not null) query = query.Where(n => n.BookId == bookId);
        var notes = await query.OrderByDescending(n => n.IsPinned).ThenBy(n => n.CreatedAt).ToListAsync(ct);

        var md = new StringBuilder("# Riwaq — Notes\n\n");
        foreach (var n in notes)
        {
            md.AppendLine($"## {n.Title}");
            if (n.PageNumber is not null) md.AppendLine($"*p. {n.PageNumber}*");
            if (n.Tags.Count > 0) md.AppendLine(string.Join(' ', n.Tags.Select(t => $"`#{t}`")));
            md.AppendLine().AppendLine(n.Content);
            if (n.Sources.Count > 0)
            {
                md.AppendLine().AppendLine("Sources:");
                foreach (var s in n.Sources) md.AppendLine($"- {s.BookTitle}, p. {s.PageStart}{(s.PageEnd != s.PageStart ? $"–{s.PageEnd}" : "")}");
            }
            md.AppendLine().AppendLine("---").AppendLine();
        }
        return ("riwaq-notes.md", md.ToString());
    }

    private async Task ApplyAsync(Guid userId, Note note, SaveNoteRequest request, CancellationToken ct)
    {
        var content = request.Content ?? "";
        if (content.Length > 100_000) throw new ValidationException("content_too_long", "The note is too long.");
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("empty_note", "A note needs a title or content.");

        int? subjectId = request.SubjectId;
        if (request.BookId is not null)
        {
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
            subjectId ??= book.SubjectId;
            if (request.ChapterId is not null && !await db.Chapters.AnyAsync(c => c.Id == request.ChapterId && c.BookId == book.Id, ct))
                throw new NotFoundException("Chapter");
        }
        if (subjectId is not null && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw new ValidationException("invalid_subject", "Unknown subject.");

        note.SubjectId = subjectId;
        note.BookId = request.BookId;
        note.ChapterId = request.BookId is null ? null : request.ChapterId;
        note.PageNumber = request.PageNumber is > 0 ? request.PageNumber : null;
        note.Title = Truncate(string.IsNullOrWhiteSpace(request.Title) ? FirstLine(content) : request.Title.Trim(), 300);
        note.Content = content;
        note.Tags = request.Tags?.Select(t => t.Trim().TrimStart('#')).Where(t => t.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList() ?? note.Tags;
        if (request.IsPinned is not null) note.IsPinned = request.IsPinned.Value;
        note.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    }

    private static string FirstLine(string content)
    {
        var line = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimStart('#', ' ') ?? "";
        return line.Length == 0 ? "Note" : line;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task<Note> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct) ?? throw new NotFoundException("Note");

    public static NoteDto ToDto(Note n) => new(n.Id, n.SubjectId, n.BookId, n.ChapterId, n.PageNumber, n.Title, n.Content, n.Tags, n.IsPinned,
        n.SourceType, n.Sources, n.CreatedAt, n.UpdatedAt);
}

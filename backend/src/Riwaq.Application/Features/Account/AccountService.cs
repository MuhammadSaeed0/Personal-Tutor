using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Retrieval;

namespace Riwaq.Application.Features.Account;

public sealed record SearchRequest(string Query, IReadOnlyList<Guid>? BookIds, int? SubjectId, Guid? ChapterId, int Limit = 20);

public sealed record SearchHitDto(long ChunkId, Guid BookId, string BookTitle, Guid? ChapterId, string? ChapterTitle, int PageStart, int PageEnd,
    string Snippet, double Score, string MatchType);

public sealed record SearchResultDto(string Query, IReadOnlyList<Guid> SearchedBookIds, IReadOnlyList<SearchHitDto> Hits);

/// <summary>Cross-book search with an explicit scope, plus account-level data export and deletion.</summary>
public sealed class AccountService(IAppDbContext db, IFileStorage storage, RetrievalService retrieval)
{
    public async Task<SearchResultDto> SearchAsync(Guid userId, SearchRequest request, CancellationToken ct)
    {
        var query = request.Query?.Trim() ?? "";
        if (query.Length is < 2 or > 500) throw new ValidationException("invalid_query", "Search for 2–500 characters.");

        var books = db.Books.AsNoTracking().Where(b => b.UserId == userId);
        if (request.BookIds is { Count: > 0 }) books = books.Where(b => request.BookIds.Contains(b.Id));
        else if (request.SubjectId is not null) books = books.Where(b => b.SubjectId == request.SubjectId);
        var scope = await books.Select(b => b.Id).ToListAsync(ct);
        if (scope.Count == 0) return new SearchResultDto(query, [], []);

        var passages = await retrieval.SearchAsync(userId, new RetrievalQuery(scope, query, null, request.ChapterId, Math.Clamp(request.Limit, 1, 50)), ct);
        return new SearchResultDto(query, scope, passages.Select(p => new SearchHitDto(p.ChunkId, p.BookId, p.BookTitle, p.ChapterId, p.ChapterTitle,
            p.PageStart, p.PageEnd, Snippet(p.Text, query), p.Score, p.MatchType)).ToList());
    }

    /// <summary>Everything the student created, as JSON (uploaded PDFs are not included; they can be re-downloaded per book).</summary>
    public async Task<object> ExportAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Email, u.DisplayName, u.CreatedAt }).FirstAsync(ct);
        return new
        {
            exportedAt = DateTime.UtcNow,
            profile = user,
            preferences = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct),
            academicProfile = await db.AcademicProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct),
            exams = await db.Exams.AsNoTracking().Where(e => e.UserId == userId).Select(e => new { e.Title, e.Date, e.SubjectId, e.Notes }).ToListAsync(ct),
            books = await db.Books.AsNoTracking().Where(b => b.UserId == userId).Select(b => new { b.Id, b.Title, b.SubjectId, b.OriginalFileName, b.PageCount, b.CreatedAt }).ToListAsync(ct),
            notes = await db.Notes.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(ct),
            highlights = await db.Highlights.AsNoTracking().Where(h => h.UserId == userId).Select(h => new { h.BookId, h.PageNumber, h.Text, h.Annotation, h.Color, h.CreatedAt }).ToListAsync(ct),
            bookmarks = await db.Bookmarks.AsNoTracking().Where(b => b.UserId == userId).Select(b => new { b.BookId, b.PageNumber, b.Label }).ToListAsync(ct),
            summaries = await db.Summaries.AsNoTracking().Where(s => s.UserId == userId).Select(s => new { s.BookId, s.Title, s.Style, s.Content, s.CreatedAt }).ToListAsync(ct),
            flashcards = await db.Flashcards.AsNoTracking().Where(f => f.UserId == userId).Select(f => new { f.Front, f.Back, f.Tags, f.SubjectId, f.State, f.Due, f.Stability, f.Difficulty, f.Reps, f.Lapses }).ToListAsync(ct),
            conversations = await db.Conversations.AsNoTracking().Where(c => c.UserId == userId)
                .Select(c => new { c.Title, c.BookId, c.CreatedAt, messages = c.Messages.OrderBy(m => m.CreatedAt).Select(m => new { m.Role, m.Content, m.CreatedAt }) }).ToListAsync(ct),
            quizAttempts = await db.QuizAttempts.AsNoTracking().Where(a => a.UserId == userId).Select(a => new { quiz = a.Quiz!.Title, a.Score, a.MaxScore, a.StartedAt, a.SubmittedAt }).ToListAsync(ct),
            studySessions = await db.StudySessions.AsNoTracking().Where(s => s.UserId == userId).Select(s => new { s.Type, s.SubjectId, s.StartedAt, s.DurationSeconds, s.Goal, s.Notes }).ToListAsync(ct),
            mastery = await db.TopicMastery.AsNoTracking().Where(t => t.UserId == userId).Select(t => new { t.TopicLabel, t.QuestionAttempts, t.QuestionCorrect, t.EstimatedMastery }).ToListAsync(ct)
        };
    }

    /// <summary>Deletes every row owned by the student and their stored files. The identity user is removed by the caller.</summary>
    public async Task DeleteAllDataAsync(Guid userId, CancellationToken ct)
    {
        var storageKeys = await db.Books.Where(b => b.UserId == userId).Select(b => b.StorageKey).ToListAsync(ct);

        await db.QuizAttempts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Quizzes.Where(q => q.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Flashcards.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Notes.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
        await db.StudyPlans.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.StudyTasks.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.StudySessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await db.TopicMastery.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Achievements.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
        await db.AiUsage.Where(u => u.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Exams.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ChapterProgress.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Conversations.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Summaries.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        // Books cascade to pages, chunks, embeddings, chapters, warnings, jobs, bookmarks, highlights and reading progress.
        await db.Books.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);

        foreach (var key in storageKeys)
        {
            try { await storage.DeleteAsync(key, ct); } catch { /* file already gone */ }
        }
    }

    private static string Snippet(string text, string query)
    {
        var clean = text.Replace('\n', ' ');
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 2);
        var index = terms.Select(t => clean.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, index - 120);
        var length = Math.Min(clean.Length - start, 360);
        return (start > 0 ? "…" : "") + clean.Substring(start, length).Trim() + (start + length < clean.Length ? "…" : "");
    }
}

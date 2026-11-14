using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Mistakes;

public sealed record MistakeDto(
    Guid Id, int SubjectId, Guid? BookId, string? BookTitle, string? ChapterTitle, Guid QuestionId, QuestionType Type, string Prompt,
    string? DiagramSvg, string? DiagramAlt, string CorrectAnswer, string Explanation, int? SourcePage,
    int TimesWrong, int CorrectStreak, DateTime NextReviewAt, bool Due, DateTime? MasteredAt, DateTime LastWrongAt);

public sealed record MistakesDto(IReadOnlyList<MistakeDto> Items, int DueCount, int OpenCount, int MasteredCount);
public sealed record RetestRequest(int SubjectId, int Count = 10);
public sealed record RetestResultDto(Guid QuizId, int QuestionCount);

/// <summary>
/// The mistakes notebook: every question answered wrongly comes back after 1 day, then 3 days (7 and 14 after more
/// correct answers) until it has been answered correctly twice in a row. Review quizzes copy the questions, so no AI call is needed.
/// </summary>
public sealed class MistakeService(IAppDbContext db, TimeProvider clock)
{
    private static readonly int[] IntervalDays = [1, 3, 7, 14];
    private const int CorrectInARowToMaster = 2;

    /// <summary>Updates the notebook for one graded question (the caller saves).</summary>
    public async Task RecordAsync(Guid userId, Quiz quiz, QuizQuestion question, bool correct, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var key = question.Id;
        if (question.OriginQuestionId is Guid origin && await db.QuizQuestions.AnyAsync(q => q.Id == origin, ct)) key = origin;

        var entry = db.Mistakes.Local.FirstOrDefault(m => m.UserId == userId && m.QuestionId == key)
                    ?? await db.Mistakes.FirstOrDefaultAsync(m => m.UserId == userId && m.QuestionId == key, ct);
        if (!correct)
        {
            if (entry is null)
            {
                entry = new MistakeEntry { Id = Ids.New(), UserId = userId, QuestionId = key, SubjectId = quiz.SubjectId, BookId = quiz.BookId, CreatedAt = now };
                db.Mistakes.Add(entry);
            }
            entry.TimesWrong++;
            entry.CorrectStreak = 0;
            entry.Stage = 0;
            entry.MasteredAt = null;
            entry.LastWrongAt = now;
            entry.NextReviewAt = now.AddDays(IntervalDays[0]);
            return;
        }

        if (entry is null || entry.MasteredAt is not null) return;
        entry.CorrectStreak++;
        if (entry.CorrectStreak >= CorrectInARowToMaster)
        {
            entry.MasteredAt = now;
            return;
        }
        entry.Stage = Math.Min(entry.Stage + 1, IntervalDays.Length - 1);
        entry.NextReviewAt = now.AddDays(IntervalDays[entry.Stage]);
    }

    public async Task<MistakesDto> ListAsync(Guid userId, int? subjectId, bool includeMastered, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.Mistakes.AsNoTracking().Where(m => m.UserId == userId);
        if (subjectId is not null) query = query.Where(m => m.SubjectId == subjectId);
        var counts = await query.GroupBy(_ => 1).Select(g => new
        {
            Due = g.Count(m => m.MasteredAt == null && m.NextReviewAt <= now),
            Open = g.Count(m => m.MasteredAt == null),
            Mastered = g.Count(m => m.MasteredAt != null)
        }).FirstOrDefaultAsync(ct);
        if (!includeMastered) query = query.Where(m => m.MasteredAt == null);

        var rows = await query.OrderBy(m => m.MasteredAt != null).ThenBy(m => m.NextReviewAt).Take(300)
            .Select(m => new { Entry = m, m.Question, Language = m.Question!.Quiz!.Language, BookTitle = m.Question.Quiz.Book!.Title })
            .ToListAsync(ct);
        var chapterIds = rows.Where(r => r.Question!.ChapterId != null).Select(r => r.Question!.ChapterId!.Value).Distinct().ToList();
        var chapters = await db.Chapters.AsNoTracking().Where(c => chapterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        var items = rows.Select(r =>
        {
            var q = r.Question!;
            var m = r.Entry;
            return new MistakeDto(m.Id, m.SubjectId, m.BookId, r.BookTitle, q.ChapterId is Guid c ? chapters.GetValueOrDefault(c) : null, q.Id, q.Type,
                q.Prompt, q.DiagramSvg, q.DiagramAlt, QuizAnswerText.Describe(q, r.Language), q.Explanation, q.SourcePage,
                m.TimesWrong, m.CorrectStreak, m.NextReviewAt, m.MasteredAt is null && m.NextReviewAt <= now, m.MasteredAt, m.LastWrongAt);
        }).ToList();
        return new MistakesDto(items, counts?.Due ?? 0, counts?.Open ?? 0, counts?.Mastered ?? 0);
    }

    public Task<int> CountDueAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.Mistakes.CountAsync(m => m.UserId == userId && m.MasteredAt == null && m.NextReviewAt <= now, ct);
    }

    /// <summary>Builds a practice quiz from the due mistakes of one subject by copying the questions (no AI call).</summary>
    public async Task<RetestResultDto> CreateRetestAsync(Guid userId, RetestRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Mistakes.AsNoTracking()
            .Where(m => m.UserId == userId && m.SubjectId == request.SubjectId && m.MasteredAt == null && m.NextReviewAt <= now)
            .OrderBy(m => m.NextReviewAt).Take(Math.Clamp(request.Count, 1, 30))
            .Select(m => new { m.Question, Language = m.Question!.Quiz!.Language, m.BookId })
            .ToListAsync(ct);
        if (due.Count == 0) throw new ConflictException("no_due_mistakes", "No mistakes are due for review in this subject.");

        var language = due.GroupBy(d => d.Language).OrderByDescending(g => g.Count()).First().Key;
        var books = due.Select(d => d.BookId).Distinct().ToList();
        var quiz = new Quiz
        {
            Id = Ids.New(), UserId = userId, SubjectId = request.SubjectId, BookId = books.Count == 1 ? books[0] : null,
            Title = language switch { "en" => "Mistakes review", "fr" => "Révision des erreurs", _ => "مراجعة الأخطاء" },
            Mode = QuizMode.Practice, Status = QuizStatus.Ready, Difficulty = Difficulty.Mixed,
            HintsAllowed = true, RevealImmediately = true, Language = language, CreatedAt = now, Model = "mistakes-notebook"
        };
        quiz.Questions = due.Select((d, i) =>
        {
            var q = d.Question!;
            return new QuizQuestion
            {
                Id = Ids.New(), QuizId = quiz.Id, Ordinal = i + 1, Type = q.Type, Prompt = q.Prompt, OptionsJson = q.OptionsJson,
                AnswerKeyJson = q.AnswerKeyJson, Explanation = q.Explanation, Hint = q.Hint, Difficulty = q.Difficulty, ChapterId = q.ChapterId,
                Topic = q.Topic, SourcePage = q.SourcePage, SourceQuote = q.SourceQuote, Points = q.Points,
                DiagramSvg = q.DiagramSvg, DiagramAlt = q.DiagramAlt, OriginQuestionId = q.Id
            };
        }).ToList();
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);
        return new RetestResultDto(quiz.Id, quiz.Questions.Count);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var deleted = await db.Mistakes.Where(m => m.Id == id && m.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Mistake");
    }
}

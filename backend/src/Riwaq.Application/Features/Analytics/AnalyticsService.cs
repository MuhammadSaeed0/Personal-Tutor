using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Learning;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Analytics;

public sealed record DailyMinutesDto(DateOnly Date, int Minutes);

public sealed record QuizPointDto(Guid AttemptId, DateTime Date, double Percent, int SubjectId);

public sealed record SubjectStatsDto(int SubjectId, int Books, int StudyMinutes30d, double? QuizAccuracy, double? EstimatedMastery, int MasteryEvidence,
    int ChaptersCompleted, int ChaptersTotal, int PagesRead, int PagesTotal, int CardsDue);

public sealed record TopicStatDto(Guid Id, int SubjectId, Guid? BookId, Guid? ChapterId, string Topic, bool IsChapter, int QuestionAttempts, int QuestionCorrect,
    double? MeasuredAccuracy, int CardReviews, double EstimatedMastery, string Band, int? LastMistakePage, DateTime UpdatedAt);

/// <summary>A data-derived recommendation. The UI localises the code and parameters.</summary>
public sealed record RecommendationDto(string Code, int Priority, Dictionary<string, object?> Params);

public sealed record OverviewDto(
    int StudyMinutes7d, int StudyMinutes30d, int CurrentStreak, int LongestStreak, int ChaptersCompleted, int ChaptersTotal,
    int QuizzesTaken30d, double? QuizAccuracy30d, int CardsReviewed30d, double? Retention30d, int WeeklyGoalMinutes,
    IReadOnlyList<DailyMinutesDto> Daily, IReadOnlyList<QuizPointDto> QuizTrend, IReadOnlyList<SubjectStatsDto> Subjects,
    IReadOnlyList<TopicStatDto> Strongest, IReadOnlyList<TopicStatDto> Weakest, IReadOnlyList<RecommendationDto> Recommendations, bool HasData);

public sealed class AnalyticsService(IAppDbContext db, TimeProvider clock)
{
    public async Task<OverviewDto> GetOverviewAsync(Guid userId, int? subjectId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var since30 = now.AddDays(-30);
        var since14 = now.Date.AddDays(-13);

        var sessions = db.StudySessions.AsNoTracking().Where(s => s.UserId == userId && s.StartedAt >= since30);
        if (subjectId is not null) sessions = sessions.Where(s => s.SubjectId == subjectId);
        var sessionRows = await sessions.Select(s => new { s.StartedAt, s.DurationSeconds, s.SubjectId }).ToListAsync(ct);

        var daily = Enumerable.Range(0, 14).Select(i =>
        {
            var day = since14.AddDays(i);
            return new DailyMinutesDto(DateOnly.FromDateTime(day), sessionRows.Where(s => s.StartedAt >= day && s.StartedAt < day.AddDays(1)).Sum(s => s.DurationSeconds) / 60);
        }).ToList();

        var attempts = db.QuizAttempts.AsNoTracking().Where(a => a.UserId == userId && a.Status == AttemptStatus.Submitted);
        if (subjectId is not null) attempts = attempts.Where(a => a.Quiz!.SubjectId == subjectId);
        var trend = await attempts.OrderByDescending(a => a.SubmittedAt).Take(15)
            .Select(a => new QuizPointDto(a.Id, a.SubmittedAt!.Value, a.MaxScore > 0 ? Math.Round(100 * a.Score / a.MaxScore, 1) : 0, a.Quiz!.SubjectId))
            .ToListAsync(ct);
        trend.Reverse();
        var recentAttempts = await attempts.Where(a => a.SubmittedAt >= since30).Select(a => new { a.Score, a.MaxScore }).ToListAsync(ct);

        var logs = db.FlashcardReviewLogs.AsNoTracking().Where(l => l.UserId == userId && l.ReviewedAt >= since30);
        if (subjectId is not null) logs = logs.Where(l => l.SubjectId == subjectId);
        var logRows = await logs.Select(l => new { l.Rating, l.StateBefore }).ToListAsync(ct);
        var mature = logRows.Where(l => l.StateBefore == CardState.Review).ToList();

        var (current, longest) = StreakCalculator.Compute(await ActiveDaysAsync(userId, ct), today);
        var subjects = await GetSubjectStatsAsync(userId, ct);
        var topics = await GetTopicsAsync(userId, subjectId, ct);
        var withEvidence = topics.Where(t => t.QuestionAttempts + t.CardReviews >= 3).ToList();
        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new UserPreferences();
        var filteredSubjects = subjectId is null ? subjects : subjects.Where(s => s.SubjectId == subjectId).ToList();

        return new OverviewDto(
            daily.Skip(7).Sum(d => d.Minutes), sessionRows.Sum(s => s.DurationSeconds) / 60, current, longest,
            filteredSubjects.Sum(s => s.ChaptersCompleted), filteredSubjects.Sum(s => s.ChaptersTotal),
            recentAttempts.Count, recentAttempts.Sum(a => a.MaxScore) > 0 ? Math.Round(100 * recentAttempts.Sum(a => a.Score) / recentAttempts.Sum(a => a.MaxScore), 1) : null,
            logRows.Count, mature.Count >= 5 ? Math.Round(100.0 * mature.Count(l => l.Rating != ReviewRating.Again) / mature.Count, 1) : null,
            prefs.WeeklyGoalMinutes, daily, trend, filteredSubjects,
            withEvidence.OrderByDescending(t => t.EstimatedMastery).Take(5).ToList(),
            withEvidence.OrderBy(t => t.EstimatedMastery).Where(t => t.EstimatedMastery < 0.6).Take(5).ToList(),
            await GetRecommendationsAsync(userId, subjectId, topics, subjects, ct),
            sessionRows.Count > 0 || trend.Count > 0 || logRows.Count > 0);
    }

    public async Task<IReadOnlyList<SubjectStatsDto>> GetSubjectStatsAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since30 = now.AddDays(-30);
        var books = await db.Books.AsNoTracking().Where(b => b.UserId == userId).Select(b => new { b.Id, b.SubjectId, b.PageCount }).ToListAsync(ct);
        var bookIds = books.Select(b => b.Id).ToList();
        var chapters = await db.Chapters.AsNoTracking().Where(c => bookIds.Contains(c.BookId) && c.Level == 1).Select(c => new { c.Id, c.BookId }).ToListAsync(ct);
        var chapterStatus = await db.ChapterProgress.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.ChapterId, p => p.Status, ct);
        var reading = await db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.BookId, p.ReadPageCount }).ToListAsync(ct);
        var minutes = await db.StudySessions.AsNoTracking().Where(s => s.UserId == userId && s.StartedAt >= since30 && s.SubjectId != null)
            .GroupBy(s => s.SubjectId!.Value).Select(g => new { g.Key, Seconds = g.Sum(s => s.DurationSeconds) }).ToDictionaryAsync(g => g.Key, g => g.Seconds, ct);
        var quiz = await db.QuizAttempts.AsNoTracking().Where(a => a.UserId == userId && a.Status == AttemptStatus.Submitted)
            .GroupBy(a => a.Quiz!.SubjectId).Select(g => new { g.Key, Score = g.Sum(a => a.Score), Max = g.Sum(a => a.MaxScore) }).ToDictionaryAsync(g => g.Key, ct);
        var mastery = await db.TopicMastery.AsNoTracking().Where(t => t.UserId == userId && t.TopicKey.StartsWith("ch:"))
            .Select(t => new { t.SubjectId, t.EstimatedMastery, Evidence = t.QuestionAttempts + t.CardReviews }).ToListAsync(ct);
        var due = await db.Flashcards.AsNoTracking().Where(f => f.UserId == userId && !f.IsSuspended && f.State != CardState.New && f.Due <= now)
            .GroupBy(f => f.SubjectId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => s.Id).ToListAsync(ct);

        return subjects.Select(id =>
        {
            var sb = books.Where(b => b.SubjectId == id).ToList();
            var ids = sb.Select(b => b.Id).ToHashSet();
            var ch = chapters.Where(c => ids.Contains(c.BookId)).ToList();
            var m = mastery.Where(x => x.SubjectId == id && x.Evidence > 0).ToList();
            var q = quiz.GetValueOrDefault(id);
            return new SubjectStatsDto(id, sb.Count, minutes.GetValueOrDefault(id) / 60,
                q is { Max: > 0 } ? Math.Round(100 * q.Score / q.Max, 1) : null,
                m.Count > 0 ? Math.Round(m.Average(x => x.EstimatedMastery), 3) : null, m.Sum(x => x.Evidence),
                ch.Count(c => chapterStatus.GetValueOrDefault(c.Id) is ChapterStatus.Completed or ChapterStatus.AlreadyKnown), ch.Count,
                reading.Where(r => ids.Contains(r.BookId)).Sum(r => r.ReadPageCount), sb.Sum(b => b.PageCount), due.GetValueOrDefault(id));
        }).ToList();
    }

    public async Task<IReadOnlyList<TopicStatDto>> GetTopicsAsync(Guid userId, int? subjectId, CancellationToken ct)
    {
        var query = db.TopicMastery.AsNoTracking().Where(t => t.UserId == userId);
        if (subjectId is not null) query = query.Where(t => t.SubjectId == subjectId);
        var rows = await query.OrderBy(t => t.SubjectId).ThenBy(t => t.TopicLabel).Take(500).ToListAsync(ct);
        return rows.Select(t => new TopicStatDto(t.Id, t.SubjectId, t.BookId, t.ChapterId, t.TopicLabel, t.TopicKey.StartsWith("ch:"),
            t.QuestionAttempts, t.QuestionCorrect, t.QuestionAttempts > 0 ? Math.Round(100.0 * t.QuestionCorrect / t.QuestionAttempts, 1) : null,
            t.CardReviews, Math.Round(t.EstimatedMastery, 3), MasteryModel.Band(t.EstimatedMastery, t.QuestionAttempts + t.CardReviews), t.LastMistakePage, t.UpdatedAt)).ToList();
    }

    /// <summary>Actionable recommendations built only from recorded performance and available content.</summary>
    private async Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(Guid userId, int? subjectId, IReadOnlyList<TopicStatDto> topics,
        IReadOnlyList<SubjectStatsDto> subjects, CancellationToken ct)
    {
        var recs = new List<RecommendationDto>();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (!subjects.Any(s => s.Books > 0))
        {
            recs.Add(new RecommendationDto("upload_first_book", 100, []));
            return recs;
        }

        // Weak concept inside a chapter that is otherwise going well ("Newton's laws fine, components not").
        foreach (var weak in topics.Where(t => !t.IsChapter && t.QuestionAttempts >= 2 && t.EstimatedMastery < 0.5).OrderBy(t => t.EstimatedMastery).Take(3))
        {
            var chapter = weak.ChapterId is null ? null : topics.FirstOrDefault(t => t.IsChapter && t.ChapterId == weak.ChapterId);
            var p = new Dictionary<string, object?>
            {
                ["topic"] = weak.Topic, ["correct"] = weak.QuestionCorrect, ["total"] = weak.QuestionAttempts, ["page"] = weak.LastMistakePage,
                ["bookId"] = weak.BookId, ["chapterId"] = weak.ChapterId, ["subjectId"] = weak.SubjectId
            };
            if (chapter is not null && chapter.EstimatedMastery >= 0.7 && chapter.QuestionAttempts >= 3)
            {
                p["chapter"] = chapter.Topic;
                p["chapterAccuracy"] = chapter.MeasuredAccuracy;
                recs.Add(new RecommendationDto("weak_concept_in_strong_chapter", 90, p));
            }
            else recs.Add(new RecommendationDto("weak_topic", 85, p));
        }

        var dueTotal = subjects.Where(s => subjectId is null || s.SubjectId == subjectId).Sum(s => s.CardsDue);
        if (dueTotal > 0)
            recs.Add(new RecommendationDto("cards_due", 80, new() { ["count"] = dueTotal, ["minutes"] = Math.Max(5, (int)Math.Ceiling(dueTotal * 0.5)) }));

        var exams = await db.Exams.AsNoTracking().Where(e => e.UserId == userId && e.Date >= today && e.Date <= today.AddDays(21)).OrderBy(e => e.Date).ToListAsync(ct);
        foreach (var exam in exams.Take(2))
        {
            var s = subjects.FirstOrDefault(x => x.SubjectId == exam.SubjectId);
            recs.Add(new RecommendationDto("exam_upcoming", 95 - exam.Date.DayNumber + today.DayNumber, new()
            {
                ["examTitle"] = exam.Title, ["days"] = exam.Date.DayNumber - today.DayNumber, ["subjectId"] = exam.SubjectId,
                ["chaptersLeft"] = s is null ? null : s.ChaptersTotal - s.ChaptersCompleted, ["mastery"] = s?.EstimatedMastery
            }));
        }

        // Chapters that have been read but never tested.
        var untested = await db.ChapterProgress.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == ChapterStatus.Completed)
            .Join(db.Chapters.AsNoTracking(), p => p.ChapterId, c => c.Id, (p, c) => new { c.Id, c.Title, c.BookId, p.CompletedAt })
            .OrderByDescending(x => x.CompletedAt).Take(10).ToListAsync(ct);
        var tested = topics.Where(t => t.IsChapter && t.QuestionAttempts > 0).Select(t => t.ChapterId).ToHashSet();
        foreach (var c in untested.Where(c => !tested.Contains(c.Id)).Take(2))
            recs.Add(new RecommendationDto("quiz_read_chapter", 70, new() { ["chapter"] = c.Title, ["chapterId"] = c.Id, ["bookId"] = c.BookId }));

        if (recs.Count == 0)
        {
            var next = subjects.Where(s => s.Books > 0 && s.ChaptersTotal > s.ChaptersCompleted).OrderBy(s => s.ChaptersCompleted / (double)Math.Max(1, s.ChaptersTotal)).FirstOrDefault();
            recs.Add(next is null
                ? new RecommendationDto("keep_reviewing", 10, [])
                : new RecommendationDto("continue_reading", 50, new() { ["subjectId"] = next.SubjectId }));
        }
        return recs.OrderByDescending(r => r.Priority).Take(6).ToList();
    }

    /// <summary>Days with real study activity: sessions, flashcard reviews or submitted quizzes.</summary>
    public async Task<List<DateOnly>> ActiveDaysAsync(Guid userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-400);
        var sessionDays = await db.StudySessions.AsNoTracking().Where(s => s.UserId == userId && s.StartedAt >= since && s.DurationSeconds >= 60).Select(s => s.StartedAt.Date).Distinct().ToListAsync(ct);
        var reviewDays = await db.FlashcardReviewLogs.AsNoTracking().Where(l => l.UserId == userId && l.ReviewedAt >= since).Select(l => l.ReviewedAt.Date).Distinct().ToListAsync(ct);
        var quizDays = await db.QuizAttempts.AsNoTracking().Where(a => a.UserId == userId && a.SubmittedAt >= since).Select(a => a.SubmittedAt!.Value.Date).Distinct().ToListAsync(ct);
        return sessionDays.Concat(reviewDays).Concat(quizDays).Select(DateOnly.FromDateTime).Distinct().ToList();
    }
}

using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Analytics;
using Riwaq.Application.Features.Books;
using Riwaq.Application.Features.Planner;
using Riwaq.Application.Features.Profile;
using Riwaq.Application.Learning;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Dashboard;

public sealed record ContinueItemDto(Guid BookId, string Title, int SubjectId, int LastPage, int PageCount, double ProgressPercent, string? ChapterTitle, DateTime? LastOpenedAt);

public sealed record RecommendedQuizDto(Guid BookId, Guid ChapterId, string ChapterTitle, int SubjectId, string Reason);

public sealed record AchievementDto(string Code, DateTime EarnedAt);

public sealed record DashboardDto(
    string DisplayName, bool OnboardingCompleted, bool HasBooks, bool MotivationEnabled,
    int CurrentStreak, int LongestStreak, int WeeklyMinutes, int WeeklyGoalMinutes,
    double? ReadingProgressPercent, int ChaptersCompleted, int ChaptersTotal,
    PlanDayDto? Today, IReadOnlyList<SubjectStatsDto> Subjects, IReadOnlyList<ExamDto> UpcomingExams,
    IReadOnlyList<ContinueItemDto> Continue, IReadOnlyList<TopicStatDto> WeakTopics, int CardsDue,
    IReadOnlyList<RecommendedQuizDto> RecommendedQuizzes, IReadOnlyList<AchievementDto> Achievements,
    IReadOnlyList<RecommendationDto> Recommendations, IReadOnlyList<ProcessingStatusDto> Processing);

public sealed class DashboardService(
    IAppDbContext db,
    AnalyticsService analytics,
    PlannerService planner,
    ProfileService profiles,
    TimeProvider clock)
{
    public async Task<DashboardDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstAsync(ct);
        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new UserPreferences();
        var academic = await db.AcademicProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new AcademicProfile();

        var books = await db.Books.AsNoTracking().Where(b => b.UserId == userId).ToListAsync(ct);
        var subjects = await analytics.GetSubjectStatsAsync(userId, ct);
        var activeDays = await analytics.ActiveDaysAsync(userId, ct);
        var (current, longest) = StreakCalculator.Compute(activeDays, today);
        var weekStart = now.Date.AddDays(-6);
        var weeklySeconds = await db.StudySessions.AsNoTracking().Where(s => s.UserId == userId && s.StartedAt >= weekStart).SumAsync(s => s.DurationSeconds, ct);

        PlanDayDto? todayPlan = null;
        if (books.Count > 0)
        {
            var plan = await planner.GetAsync(userId, ct);
            todayPlan = plan.Days.FirstOrDefault(d => d.Date == today) ?? new PlanDayDto(today, 0, 0, []);
        }

        var progress = await db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);
        var continueItems = new List<ContinueItemDto>();
        foreach (var book in books.Where(b => b.LastOpenedAt != null && b.Status is BookStatus.Ready or BookStatus.ReadyWithWarnings).OrderByDescending(b => b.LastOpenedAt).Take(4))
        {
            var p = progress.FirstOrDefault(x => x.BookId == book.Id);
            var lastPage = p?.LastPage ?? 1;
            var chapter = await db.Chapters.AsNoTracking().Where(c => c.BookId == book.Id && c.StartPage <= lastPage && c.EndPage >= lastPage)
                .OrderByDescending(c => c.Level).Select(c => c.Title).FirstOrDefaultAsync(ct);
            continueItems.Add(new ContinueItemDto(book.Id, book.Title, book.SubjectId, lastPage, book.PageCount,
                book.PageCount > 0 ? Math.Round(100.0 * (p?.ReadPageCount ?? 0) / book.PageCount, 1) : 0, chapter, book.LastOpenedAt));
        }

        var topics = await analytics.GetTopicsAsync(userId, null, ct);
        var weak = topics.Where(t => t.QuestionAttempts + t.CardReviews >= 2 && t.EstimatedMastery < 0.5).OrderBy(t => t.EstimatedMastery).Take(3).ToList();

        var tested = topics.Where(t => t.IsChapter && t.QuestionAttempts > 0).Select(t => t.ChapterId).ToHashSet();
        var readChapters = await db.ChapterProgress.AsNoTracking().Where(p => p.UserId == userId && p.Status == ChapterStatus.Completed)
            .Join(db.Chapters.AsNoTracking(), p => p.ChapterId, c => c.Id, (p, c) => new { c.Id, c.Title, c.BookId, p.CompletedAt })
            .OrderByDescending(x => x.CompletedAt).Take(10).ToListAsync(ct);
        var recommendedQuizzes = readChapters.Where(c => !tested.Contains(c.Id)).Take(3)
            .Select(c => new RecommendedQuizDto(c.BookId, c.Id, c.Title, books.First(b => b.Id == c.BookId).SubjectId, "chapter_read_not_tested")).ToList();

        var achievements = prefs.MotivationEnabled ? await EvaluateAchievementsAsync(userId, books.Count, subjects, longest, ct) : [];
        var overview = await analytics.GetOverviewAsync(userId, null, ct);

        var processingIds = books.Where(b => b.Status is BookStatus.Uploaded or BookStatus.Processing).Select(b => b.Id).ToList();
        var jobs = await db.ProcessingJobs.AsNoTracking().Where(j => processingIds.Contains(j.BookId)).GroupBy(j => j.BookId)
            .Select(g => g.OrderByDescending(j => j.CreatedAt).First()).ToListAsync(ct);

        var totalPages = subjects.Sum(s => s.PagesTotal);
        return new DashboardDto(user, academic.OnboardingCompleted, books.Count > 0, prefs.MotivationEnabled, current, longest, weeklySeconds / 60,
            prefs.WeeklyGoalMinutes, totalPages > 0 ? Math.Round(100.0 * subjects.Sum(s => s.PagesRead) / totalPages, 1) : null,
            subjects.Sum(s => s.ChaptersCompleted), subjects.Sum(s => s.ChaptersTotal), todayPlan, subjects,
            await profiles.GetExamsAsync(userId, upcomingOnly: true, ct), continueItems, weak, subjects.Sum(s => s.CardsDue), recommendedQuizzes,
            achievements, overview.Recommendations,
            books.Where(b => processingIds.Contains(b.Id)).Select(b => BookMappings.ToStatus(b, jobs.FirstOrDefault(j => j.BookId == b.Id))).ToList());
    }

    public async Task<IReadOnlyList<AchievementDto>> GetAchievementsAsync(Guid userId, CancellationToken ct) =>
        await db.Achievements.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.EarnedAt)
            .Select(a => new AchievementDto(a.Code, a.EarnedAt)).ToListAsync(ct);

    /// <summary>Awards milestone achievements based on real activity. Private to the student; no rankings.</summary>
    private async Task<IReadOnlyList<AchievementDto>> EvaluateAchievementsAsync(Guid userId, int bookCount, IReadOnlyList<SubjectStatsDto> subjects, int longestStreak, CancellationToken ct)
    {
        var earned = await db.Achievements.Where(a => a.UserId == userId).Select(a => a.Code).ToListAsync(ct);
        var chapters = subjects.Sum(s => s.ChaptersCompleted);
        var reviews = await db.FlashcardReviewLogs.CountAsync(l => l.UserId == userId, ct);
        var quizzes = await db.QuizAttempts.Where(a => a.UserId == userId && a.Status == AttemptStatus.Submitted).Select(a => new { a.Score, a.MaxScore }).ToListAsync(ct);
        var focusSeconds = await db.StudySessions.Where(s => s.UserId == userId && s.Type == StudySessionType.Focus).SumAsync(s => s.DurationSeconds, ct);
        var notes = await db.Notes.CountAsync(n => n.UserId == userId, ct);

        var conditions = new Dictionary<string, bool>
        {
            ["first_book"] = bookCount >= 1,
            ["first_chapter"] = chapters >= 1,
            ["chapters_10"] = chapters >= 10,
            ["streak_3"] = longestStreak >= 3,
            ["streak_7"] = longestStreak >= 7,
            ["streak_30"] = longestStreak >= 30,
            ["reviews_100"] = reviews >= 100,
            ["reviews_1000"] = reviews >= 1000,
            ["first_quiz"] = quizzes.Count >= 1,
            ["quiz_perfect"] = quizzes.Any(q => q.MaxScore > 0 && q.Score >= q.MaxScore),
            ["focus_10h"] = focusSeconds >= 36_000,
            ["notes_10"] = notes >= 10
        };

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var (code, met) in conditions)
            if (met && !earned.Contains(code)) db.Achievements.Add(new Achievement { Id = Ids.New(), UserId = userId, Code = code, EarnedAt = now });
        await db.SaveChangesAsync(ct);

        return await GetAchievementsAsync(userId, ct);
    }
}

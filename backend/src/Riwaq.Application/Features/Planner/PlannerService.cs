using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Planner;

public sealed record StudyTaskDto(Guid Id, DateOnly Date, int Order, StudyTaskType Type, StudyTaskStatus Status, int? SubjectId, Guid? BookId,
    string? BookTitle, Guid? ChapterId, string? ChapterTitle, string? Topic, int EstimatedMinutes, string ReasonCode, int? PageStart, int? PageEnd, DateTime? CompletedAt);

public sealed record PlanDayDto(DateOnly Date, int PlannedMinutes, int CompletedMinutes, IReadOnlyList<StudyTaskDto> Tasks);

public sealed record PlanDto(Guid? PlanId, DateTime? GeneratedAt, int DailyMinutes, int BreakEveryMinutes, int BreakMinutes, IReadOnlyList<PlanDayDto> Days);

public sealed record UpdateTaskRequest(StudyTaskStatus? Status, DateOnly? Date);

/// <summary>Planner inputs gathered from the database (kept separate so the algorithm is unit-testable).</summary>
public sealed record PlannerInput(
    DateOnly Today,
    int DailyMinutes,
    IReadOnlyList<int> DifficultSubjectIds,
    IReadOnlyList<PlannerExam> Exams,
    IReadOnlyList<PlannerChapter> Chapters,
    IReadOnlyList<PlannerTopic> WeakTopics,
    IReadOnlyDictionary<int, int> DueCardsBySubject,
    IReadOnlyDictionary<int, double> SubjectMastery,
    IReadOnlySet<int> SubjectsStudiedToday,
    int Days = 7);

public sealed record PlannerExam(int? SubjectId, DateOnly Date);

public sealed record PlannerChapter(int SubjectId, Guid BookId, Guid ChapterId, int Order, int StartPage, int EndPage, ChapterStatus Status, bool Quizzed);

public sealed record PlannerTopic(int SubjectId, Guid? BookId, Guid? ChapterId, string Label, double Mastery, int? Page);

public sealed record PlannedTask(DateOnly Date, int Order, StudyTaskType Type, int? SubjectId, Guid? BookId, Guid? ChapterId, string? Topic,
    int Minutes, string ReasonCode, double Priority, int? PageStart, int? PageEnd);

/// <summary>
/// Builds a realistic 7-day plan: never schedules more than the student's daily availability, reserves time
/// for due flashcards, rotates subjects, and prioritises by exam urgency, measured weakness, self-reported
/// difficulty and remaining material. Regenerating replaces only future pending tasks, so missed work is
/// re-prioritised instead of piling up.
/// </summary>
public static class PlanningAlgorithm
{
    private const int MinutesPerPage = 3;
    private const int MaxReadingBlock = 45;
    private const int MinTask = 10;

    public static List<PlannedTask> Build(PlannerInput input)
    {
        var tasks = new List<PlannedTask>();
        var capacity = Math.Clamp(input.DailyMinutes, 30, 720);
        var usable = (int)(capacity * 0.9); // keep a buffer so the plan stays achievable

        // Reading cursors per book: the next unread page in the next unfinished chapter.
        var pending = input.Chapters
            .Where(c => c.Status is ChapterStatus.NotStarted or ChapterStatus.InProgress)
            .OrderBy(c => c.SubjectId).ThenBy(c => c.BookId).ThenBy(c => c.Order)
            .GroupBy(c => c.SubjectId)
            .ToDictionary(g => g.Key, g => new Queue<PlannerChapter>(g));
        var cursor = new Dictionary<Guid, int>(); // chapter → next page
        var quizCandidates = input.Chapters.Where(c => c.Status == ChapterStatus.Completed && !c.Quizzed)
            .GroupBy(c => c.SubjectId).ToDictionary(g => g.Key, g => new Queue<PlannerChapter>(g));
        var weak = input.WeakTopics.OrderBy(t => t.Mastery).GroupBy(t => t.SubjectId).ToDictionary(g => g.Key, g => new Queue<PlannerTopic>(g));

        var subjects = input.Chapters.Select(c => c.SubjectId)
            .Concat(input.DueCardsBySubject.Keys).Concat(input.Exams.Where(e => e.SubjectId != null).Select(e => e.SubjectId!.Value))
            .Distinct().ToList();

        for (var day = 0; day < input.Days; day++)
        {
            var date = input.Today.AddDays(day);
            var remaining = usable;
            var order = 0;
            void Add(StudyTaskType type, int? subject, Guid? book, Guid? chapter, string? topic, int minutes, string reason, double priority, int? from = null, int? to = null)
            {
                tasks.Add(new PlannedTask(date, order++, type, subject, book, chapter, topic, minutes, reason, Math.Round(priority, 3), from, to));
                remaining -= minutes;
            }

            // 1. Flashcard reviews (≈30 s per card), capped at 30% of the day.
            foreach (var (subject, due) in input.DueCardsBySubject.Where(d => d.Value > 0).OrderByDescending(d => d.Value))
            {
                var expected = day == 0 ? due : Math.Max(0, due / (day + 2));
                if (expected == 0) continue;
                var minutes = Math.Clamp((int)Math.Ceiling(expected * 0.5), MinTask, Math.Max(MinTask, capacity * 3 / 10));
                if (minutes > remaining) break;
                Add(StudyTaskType.ReviewFlashcards, subject, null, null, null, minutes, "cards_due", 10);
            }

            // 2. Subject priorities for the day (rotation lowers a subject after it gets a task).
            var priority = subjects.ToDictionary(s => s, s => SubjectPriority(s, date, input) * (day == 0 && input.SubjectsStudiedToday.Contains(s) ? 0.8 : 1));
            var guard = 0;
            while (remaining >= MinTask && priority.Count > 0 && guard++ < 50)
            {
                var (subject, score) = priority.OrderByDescending(p => p.Value).First();
                var examDays = DaysToExam(subject, date, input);
                var scheduled = false;

                if (examDays is >= 0 and <= 7 && remaining >= 30 && !tasks.Any(t => t.Date == date && t.SubjectId == subject && t.Type == StudyTaskType.ExamPreparation))
                {
                    var anyChapter = input.Chapters.FirstOrDefault(c => c.SubjectId == subject);
                    if (anyChapter is not null)
                    {
                        Add(StudyTaskType.ExamPreparation, subject, anyChapter.BookId, null, null, Math.Min(45, remaining), "exam_soon", score);
                        scheduled = true;
                    }
                }

                if (!scheduled && weak.TryGetValue(subject, out var topics) && topics.Count > 0 && remaining >= 15)
                {
                    var t = topics.Dequeue();
                    Add(StudyTaskType.ReviseWeakTopic, subject, t.BookId, t.ChapterId, t.Label, 20 <= remaining ? 20 : remaining, "weak_topic", score, t.Page, t.Page);
                    scheduled = true;
                }
                else if (!scheduled && quizCandidates.TryGetValue(subject, out var qc) && qc.Count > 0 && remaining >= 15)
                {
                    var c = qc.Dequeue();
                    Add(StudyTaskType.PracticeQuiz, subject, c.BookId, c.ChapterId, null, 15, "check_understanding", score);
                    scheduled = true;
                }
                else if (!scheduled && pending.TryGetValue(subject, out var queue) && queue.Count > 0)
                {
                    var chapter = queue.Peek();
                    var key = CursorKey(chapter);
                    var start = cursor.GetValueOrDefault(key, chapter.StartPage);
                    var pagesLeft = chapter.EndPage - start + 1;
                    var minutes = Math.Min(Math.Min(MaxReadingBlock, remaining), pagesLeft * MinutesPerPage);
                    if (minutes >= MinTask || pagesLeft * MinutesPerPage < MinTask)
                    {
                        var pages = Math.Max(1, minutes / MinutesPerPage);
                        var end = Math.Min(chapter.EndPage, start + pages - 1);
                        Add(StudyTaskType.Read, subject, chapter.BookId, chapter.ChapterId == Guid.Empty ? null : chapter.ChapterId, null, Math.Max(MinTask, (end - start + 1) * MinutesPerPage),
                            examDays is >= 0 and <= 21 ? "exam_soon" : "next_chapter", score, start, end);
                        if (end >= chapter.EndPage)
                        {
                            queue.Dequeue();
                            cursor.Remove(key);
                            // A freshly read chapter becomes a quiz candidate for later days.
                            if (chapter.ChapterId != Guid.Empty)
                            {
                                if (!quizCandidates.TryGetValue(subject, out var q2)) quizCandidates[subject] = q2 = new Queue<PlannerChapter>();
                                q2.Enqueue(chapter);
                            }
                        }
                        else cursor[key] = end + 1;
                        scheduled = true;
                    }
                }

                if (scheduled) priority[subject] = score * 0.55;
                else priority.Remove(subject);
            }
        }
        return tasks;
    }

    /// <summary>Books without detected chapters use the book id as their reading cursor.</summary>
    private static Guid CursorKey(PlannerChapter c) => c.ChapterId == Guid.Empty ? c.BookId : c.ChapterId;

    public static double SubjectPriority(int subjectId, DateOnly date, PlannerInput input)
    {
        var days = DaysToExam(subjectId, date, input);
        var urgency = days is null ? 1 : 1 + 10.0 / (days.Value + 1) * (days <= 14 ? 1.5 : 1);
        var weakness = 1 - input.SubjectMastery.GetValueOrDefault(subjectId, 0.5);
        var difficult = input.DifficultSubjectIds.Contains(subjectId) ? 1 : 0;
        var chapters = input.Chapters.Where(c => c.SubjectId == subjectId).ToList();
        var backlog = chapters.Count == 0 ? 0.3 : chapters.Count(c => c.Status is ChapterStatus.NotStarted or ChapterStatus.InProgress) / (double)chapters.Count;
        return urgency * (0.4 + weakness * 0.6) * (1 + 0.5 * difficult) * (0.5 + backlog);
    }

    private static int? DaysToExam(int subjectId, DateOnly date, PlannerInput input)
    {
        var next = input.Exams.Where(e => (e.SubjectId == subjectId || e.SubjectId == null) && e.Date >= date).OrderBy(e => e.Date).FirstOrDefault();
        return next is null ? null : next.Date.DayNumber - date.DayNumber;
    }
}

public sealed class PlannerService(IAppDbContext db, TimeProvider clock)
{
    public async Task<PlanDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var today = Today();
        var hasFuture = await db.StudyTasks.AnyAsync(t => t.UserId == userId && t.Date >= today, ct);
        if (!hasFuture && await db.Books.AnyAsync(b => b.UserId == userId, ct)) return await GenerateAsync(userId, ct);
        return await BuildDtoAsync(userId, ct);
    }

    public async Task<PlanDto> GenerateAsync(Guid userId, CancellationToken ct)
    {
        var today = Today();
        var now = clock.GetUtcNow().UtcDateTime;

        // Missed tasks are closed as skipped (not carried over) and future pending tasks are replaced.
        await db.StudyTasks.Where(t => t.UserId == userId && t.Date < today && t.Status == StudyTaskStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, StudyTaskStatus.Skipped), ct);
        await db.StudyTasks.Where(t => t.UserId == userId && t.Date >= today && t.Status == StudyTaskStatus.Pending).ExecuteDeleteAsync(ct);

        var input = await LoadInputAsync(userId, today, ct);
        var completedToday = await db.StudyTasks.Where(t => t.UserId == userId && t.Date == today && t.Status == StudyTaskStatus.Completed)
            .SumAsync(t => t.EstimatedMinutes, ct);
        var planned = PlanningAlgorithm.Build(input with { DailyMinutes = input.DailyMinutes });
        // Time already completed today counts against today's availability.
        var todayBudget = (int)(Math.Clamp(input.DailyMinutes, 30, 720) * 0.9) - completedToday;
        var used = 0;
        planned = planned.Where(t =>
        {
            if (t.Date != today) return true;
            used += t.Minutes;
            return used <= Math.Max(0, todayBudget);
        }).ToList();

        var plan = new StudyPlan
        {
            Id = Ids.New(), UserId = userId, StartDate = today, EndDate = today.AddDays(input.Days - 1), DailyMinutes = input.DailyMinutes, GeneratedAt = now,
            Tasks = planned.Select(t => new StudyTask
            {
                Id = Ids.New(), UserId = userId, Date = t.Date, Order = t.Order, Type = t.Type, Status = StudyTaskStatus.Pending,
                SubjectId = t.SubjectId, BookId = t.BookId, ChapterId = t.ChapterId,
                Topic = t.PageStart is null ? t.Topic : $"{t.Topic}|{t.PageStart}-{t.PageEnd}",
                EstimatedMinutes = t.Minutes, ReasonCode = t.ReasonCode, Priority = t.Priority
            }).ToList()
        };
        db.StudyPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return await BuildDtoAsync(userId, ct);
    }

    public async Task<StudyTaskDto> UpdateTaskAsync(Guid userId, Guid taskId, UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await db.StudyTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct) ?? throw new NotFoundException("Task");
        if (request.Status is not null)
        {
            task.Status = request.Status.Value;
            task.CompletedAt = request.Status == StudyTaskStatus.Completed ? clock.GetUtcNow().UtcDateTime : null;
        }
        if (request.Date is not null)
        {
            var today = Today();
            if (request.Date < today || request.Date > today.AddDays(30)) throw new ValidationException("invalid_date", "Reschedule within the next 30 days.");
            task.Date = request.Date.Value;
            task.Order = 1000; // after the day's existing tasks
        }
        await db.SaveChangesAsync(ct);
        var dto = await BuildDtoAsync(userId, ct);
        return dto.Days.SelectMany(d => d.Tasks).First(t => t.Id == taskId);
    }

    public async Task<PlannerInput> LoadInputAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var profile = await db.AcademicProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new AcademicProfile();
        var exams = await db.Exams.AsNoTracking().Where(e => e.UserId == userId && e.Date >= today).Select(e => new PlannerExam(e.SubjectId, e.Date)).ToListAsync(ct);

        var books = await db.Books.AsNoTracking().Where(b => b.UserId == userId && (b.Status == BookStatus.Ready || b.Status == BookStatus.ReadyWithWarnings))
            .Select(b => new { b.Id, b.SubjectId }).ToListAsync(ct);
        var bookIds = books.Select(b => b.Id).ToList();
        var chapters = await db.Chapters.AsNoTracking().Where(c => bookIds.Contains(c.BookId) && c.Level == 1).ToListAsync(ct);
        var status = await db.ChapterProgress.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.ChapterId, p => p.Status, ct);
        var mastery = await db.TopicMastery.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(ct);
        var quizzedChapters = mastery.Where(m => m.ChapterId != null && m.QuestionAttempts > 0).Select(m => m.ChapterId!.Value).ToHashSet();

        var plannerChapters = chapters.Select(c => new PlannerChapter(books.First(b => b.Id == c.BookId).SubjectId, c.BookId, c.Id, c.Order, c.StartPage, c.EndPage,
            status.GetValueOrDefault(c.Id, ChapterStatus.NotStarted), quizzedChapters.Contains(c.Id))).ToList();

        // Books without detected chapters are planned as one block so they are not ignored.
        foreach (var book in books.Where(b => chapters.All(c => c.BookId != b.Id)))
        {
            var pageCount = await db.Books.Where(b => b.Id == book.Id).Select(b => b.PageCount).FirstAsync(ct);
            var read = await db.ReadingProgress.AsNoTracking().Where(p => p.UserId == userId && p.BookId == book.Id).Select(p => p.LastPage).FirstOrDefaultAsync(ct);
            if (pageCount > 0 && read < pageCount)
                plannerChapters.Add(new PlannerChapter(book.SubjectId, book.Id, Guid.Empty, 0, Math.Max(1, read), pageCount, ChapterStatus.InProgress, false));
        }

        var weak = mastery.Where(m => m.TopicKey.StartsWith("t:") && m.EstimatedMastery < 0.5 && m.QuestionAttempts + m.CardReviews >= 2)
            .Select(m => new PlannerTopic(m.SubjectId, m.BookId, m.ChapterId, m.TopicLabel, m.EstimatedMastery, m.LastMistakePage)).ToList();

        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Flashcards.AsNoTracking().Where(f => f.UserId == userId && !f.IsSuspended && f.State != CardState.New && f.Due <= now.AddDays(1))
            .GroupBy(f => f.SubjectId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var subjectMastery = mastery.Where(m => m.QuestionAttempts + m.CardReviews >= 3).GroupBy(m => m.SubjectId)
            .ToDictionary(g => g.Key, g => g.Average(m => m.EstimatedMastery));
        var studiedToday = await db.StudySessions.AsNoTracking().Where(s => s.UserId == userId && s.StartedAt >= now.Date && s.SubjectId != null)
            .Select(s => s.SubjectId!.Value).Distinct().ToListAsync(ct);

        return new PlannerInput(today, profile.DailyStudyMinutes, profile.DifficultSubjectIds, exams,
            plannerChapters.Where(c => c.ChapterId != Guid.Empty || c.EndPage > 0).ToList(), weak, due, subjectMastery, studiedToday.ToHashSet());
    }

    private async Task<PlanDto> BuildDtoAsync(Guid userId, CancellationToken ct)
    {
        var today = Today();
        var plan = await db.StudyPlans.AsNoTracking().Where(p => p.UserId == userId).OrderByDescending(p => p.GeneratedAt).FirstOrDefaultAsync(ct);
        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new UserPreferences();
        var profile = await db.AcademicProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new AcademicProfile();
        var tasks = await db.StudyTasks.AsNoTracking().Where(t => t.UserId == userId && t.Date >= today && t.Date <= today.AddDays(13))
            .OrderBy(t => t.Date).ThenBy(t => t.Order).ToListAsync(ct);

        var bookIds = tasks.Where(t => t.BookId != null).Select(t => t.BookId!.Value).Distinct().ToList();
        var chapterIds = tasks.Where(t => t.ChapterId != null).Select(t => t.ChapterId!.Value).Distinct().ToList();
        var books = await db.Books.AsNoTracking().Where(b => bookIds.Contains(b.Id) && b.UserId == userId).ToDictionaryAsync(b => b.Id, b => b.Title, ct);
        var chapters = await db.Chapters.AsNoTracking().Where(c => chapterIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        var days = tasks.Where(t => t.BookId is null || books.ContainsKey(t.BookId.Value)).GroupBy(t => t.Date).Select(g => new PlanDayDto(g.Key,
            g.Sum(t => t.EstimatedMinutes), g.Where(t => t.Status == StudyTaskStatus.Completed).Sum(t => t.EstimatedMinutes),
            g.Select(t => ToDto(t, books, chapters)).ToList())).ToList();

        return new PlanDto(plan?.Id, plan?.GeneratedAt, profile.DailyStudyMinutes, 50, prefs.BreakMinutes, days);
    }

    private static StudyTaskDto ToDto(StudyTask t, Dictionary<Guid, string> books, Dictionary<Guid, string> chapters)
    {
        string? topic = t.Topic;
        int? from = null, to = null;
        if (topic?.Contains('|') == true)
        {
            var parts = topic.Split('|');
            topic = parts[0].Length == 0 ? null : parts[0];
            var range = parts[1].Split('-');
            if (range.Length == 2 && int.TryParse(range[0], out var a) && int.TryParse(range[1], out var b)) (from, to) = (a, b);
        }
        return new StudyTaskDto(t.Id, t.Date, t.Order, t.Type, t.Status, t.SubjectId, t.BookId, t.BookId is { } bid ? books.GetValueOrDefault(bid) : null,
            t.ChapterId, t.ChapterId is { } cid ? chapters.GetValueOrDefault(cid) : null, topic, t.EstimatedMinutes, t.ReasonCode, from, to, t.CompletedAt);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

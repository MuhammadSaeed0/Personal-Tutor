using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Flashcards;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Application.Features.Summaries;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.StudyPacks;

public sealed record StudyPackDto(Guid Id, Guid BookId, StudyPackStatus Status, int Total, int Done, int Summaries, int Cards, int Quizzes,
    string? ErrorCode, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// The study pack of a new source: for each of its first chapters (at most <see cref="MaxChapters"/>), a quick summary,
/// a handful of flashcards and a short quiz, made in the background after processing. A chapter that fails is skipped;
/// reaching the daily AI limit stops the pack.
/// </summary>
public sealed class StudyPackService(
    IAppDbContext db,
    SummaryService summaries,
    FlashcardService flashcards,
    QuizService quizzes,
    IStudyPackSignal signal,
    IRealtimeNotifier notifier,
    TimeProvider clock,
    ILogger<StudyPackService> logger)
{
    public const int MaxChapters = 10;
    private const int CardsPerChapter = 8;
    private const int QuestionsPerQuiz = 5;

    public async Task<StudyPackDto> RequestAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
        var open = await db.StudyPacks.AsNoTracking().Where(p => p.BookId == book.Id && (p.Status == StudyPackStatus.Pending || p.Status == StudyPackStatus.Running))
            .FirstOrDefaultAsync(ct);
        if (open is not null) return ToDto(open);

        var now = clock.GetUtcNow().UtcDateTime;
        var pack = new StudyPack { Id = Ids.New(), UserId = userId, BookId = book.Id, Status = StudyPackStatus.Pending, CreatedAt = now, UpdatedAt = now };
        db.StudyPacks.Add(pack);
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return ToDto(pack);
    }

    public async Task<StudyPackDto?> GetAsync(Guid userId, Guid bookId, CancellationToken ct)
    {
        var pack = await db.StudyPacks.AsNoTracking().Where(p => p.BookId == bookId && p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        return pack is null ? null : ToDto(pack);
    }

    /// <summary>The next pack whose source is ready, or null. Used by the background worker.</summary>
    public Task<Guid?> NextRunnableAsync(CancellationToken ct) =>
        db.StudyPacks.AsNoTracking()
            .Where(p => (p.Status == StudyPackStatus.Pending || p.Status == StudyPackStatus.Running)
                        && (p.Book!.Status == BookStatus.Ready || p.Book.Status == BookStatus.ReadyWithWarnings))
            .OrderBy(p => p.CreatedAt).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

    /// <summary>A pack whose source failed to process cannot run.</summary>
    public async Task FailBrokenAsync(CancellationToken ct)
    {
        await db.StudyPacks.Where(p => (p.Status == StudyPackStatus.Pending || p.Status == StudyPackStatus.Running) && p.Book!.Status == BookStatus.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StudyPackStatus.Failed).SetProperty(p => p.ErrorCode, "processing_failed"), ct);
    }

    public async Task RunAsync(Guid packId, CancellationToken ct)
    {
        var pack = await db.StudyPacks.FirstOrDefaultAsync(p => p.Id == packId, ct);
        if (pack is null || pack.Status is StudyPackStatus.Completed or StudyPackStatus.Failed) return;
        var userId = pack.UserId;

        var chapters = await db.Chapters.AsNoTracking().Where(c => c.BookId == pack.BookId && c.Level == 1)
            .OrderBy(c => c.Order).Take(MaxChapters).Select(c => new { c.Id, c.Title }).ToListAsync(ct);
        // A source without chapters is treated as one unit.
        var units = chapters.Count > 0 ? chapters.Select(c => (Guid?)c.Id).ToList() : [null];
        pack.Status = StudyPackStatus.Running;
        pack.Total = units.Count;
        await SaveAndNotifyAsync(pack, ct);

        for (var i = pack.Done; i < units.Count; i++)
        {
            var chapterId = units[i];
            var scope = chapterId is null ? SummaryScope.Book : SummaryScope.Chapter;
            try
            {
                await Step(() => summaries.GenerateAsync(userId, new GenerateSummaryRequest(pack.BookId, scope, SummaryStyle.Quick, ChapterId: chapterId), ct),
                    () => pack.Summaries++);

                await Step(async () =>
                {
                    var drafts = await flashcards.GenerateAsync(userId, new GenerateFlashcardsRequest(pack.BookId, scope, chapterId, Count: CardsPerChapter), ct);
                    if (drafts.Drafts.Count == 0) return 0;
                    var saved = await flashcards.CreateBatchAsync(userId, new BatchCreateRequest(drafts.Drafts.Select(d =>
                        new CreateFlashcardRequest(drafts.SubjectId, drafts.BookId, drafts.ChapterId, d.Front, d.Back, d.Tags, ContentSourceType.AiGenerated, d.Page)).ToList()), ct);
                    return saved.Count;
                }, n => pack.Cards += n);

                await Step(() => quizzes.EnqueueAsync(userId, new GenerateQuizRequest(pack.BookId, chapterId, Difficulty: Difficulty.Mixed, Count: QuestionsPerQuiz),
                    limitParallel: false, ct), () => pack.Quizzes++);
            }
            catch (AiUnavailableException ex) when (ex.Code is "ai_quota_exceeded" or "ai_not_configured")
            {
                // Nothing more can be made today: keep what was made and say why it stopped.
                ((DbContext)db).ChangeTracker.Clear();
                pack = (await db.StudyPacks.FirstAsync(p => p.Id == packId, ct));
                pack.Status = StudyPackStatus.Failed;
                pack.ErrorCode = ex.Code;
                await SaveAndNotifyAsync(pack, ct);
                await NotifyFinishedAsync(pack, ct);
                return;
            }

            pack.Done = i + 1;
            await SaveAndNotifyAsync(pack, ct);
        }

        pack.Status = StudyPackStatus.Completed;
        await SaveAndNotifyAsync(pack, ct);
        await NotifyFinishedAsync(pack, ct);
    }

    private Task Step<T>(Func<Task<T>> action, Action counted) => Step(action, _ => counted());

    /// <summary>Runs one part of a chapter's pack; a failure skips that part only (the daily limit stops the whole pack).</summary>
    private async Task Step<T>(Func<Task<T>> action, Action<T> counted)
    {
        try
        {
            counted(await action());
        }
        catch (AiUnavailableException ex) when (ex.Code is "ai_quota_exceeded" or "ai_not_configured")
        {
            throw;
        }
        catch (Exception ex) when (ex is AiUnavailableException or AppException)
        {
            logger.LogWarning("Study pack step skipped: {Code}", ex is AiUnavailableException ai ? ai.Code : ((AppException)ex).Code);
            ((DbContext)db).ChangeTracker.Entries().Where(e => e.Entity is not StudyPack && e.State != EntityState.Unchanged).ToList()
                .ForEach(e => e.State = EntityState.Detached);
        }
    }

    private async Task SaveAndNotifyAsync(StudyPack pack, CancellationToken ct)
    {
        pack.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await notifier.SendToUserAsync(pack.UserId, "studyPackUpdated", ToDto(pack), CancellationToken.None);
    }

    private async Task NotifyFinishedAsync(StudyPack pack, CancellationToken ct)
    {
        var title = await db.Books.AsNoTracking().Where(b => b.Id == pack.BookId).Select(b => b.Title).FirstOrDefaultAsync(ct);
        db.Notifications.Add(new Notification
        {
            Id = Ids.New(), UserId = pack.UserId, Type = pack.Status == StudyPackStatus.Completed ? "study_pack_ready" : "study_pack_stopped",
            EntityId = pack.BookId.ToString(), Data = title, CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    private static StudyPackDto ToDto(StudyPack p) =>
        new(p.Id, p.BookId, p.Status, p.Total, p.Done, p.Summaries, p.Cards, p.Quizzes, p.ErrorCode, p.CreatedAt, p.UpdatedAt);
}

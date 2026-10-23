using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Ai;
using Riwaq.Application.Features.Progress;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Application.Features.Summaries;
using Riwaq.Application.Learning;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Flashcards;

public sealed record FlashcardDto(
    Guid Id, int SubjectId, Guid? BookId, Guid? ChapterId, string Front, string Back, IReadOnlyList<string> Tags,
    ContentSourceType SourceType, int? SourcePage, CardState State, DateTime Due, double Stability, double Difficulty,
    int Reps, int Lapses, bool IsMastered, bool IsSuspended, DateTime? LastReview, DateTime CreatedAt);

public sealed record CreateFlashcardRequest(
    int? SubjectId, Guid? BookId, Guid? ChapterId, string Front, string Back, IReadOnlyList<string>? Tags,
    ContentSourceType SourceType = ContentSourceType.Manual, int? SourcePage = null);

public sealed record UpdateFlashcardRequest(string? Front, string? Back, IReadOnlyList<string>? Tags, bool? IsSuspended);

public sealed record BatchCreateRequest(IReadOnlyList<CreateFlashcardRequest> Cards);

public sealed record ReviewRequest(ReviewRating Rating, int? DurationMs);

public sealed record ReviewResultDto(FlashcardDto Card, double ScheduledDays, double? PredictedRecall);

public sealed record IntervalPreviewDto(double Again, double Hard, double Good, double Easy);

public sealed record GenerateFlashcardsRequest(
    Guid BookId, SummaryScope Scope, Guid? ChapterId = null, int? Page = null, int? PageStart = null, int? PageEnd = null,
    string? SelectedText = null, Guid? SummaryId = null, int Count = 10, string? Focus = null, string? Language = null);

public sealed record FlashcardDraft(string Front, string Back, int? Page, IReadOnlyList<string> Tags);

public sealed record GeneratedFlashcardsDto(int SubjectId, Guid BookId, Guid? ChapterId, IReadOnlyList<FlashcardDraft> Drafts, IReadOnlyList<string> Warnings);

public sealed record FlashcardStatsDto(int Total, int DueNow, int New, int Learning, int Review, int Mastered, int ReviewsToday,
    double? Retention30d, int ReviewStreak, IReadOnlyList<DailyCount> Forecast);

public sealed record DailyCount(DateOnly Date, int Count);

public sealed record DueQueueDto(IReadOnlyList<FlashcardDto> Cards, int DueCount, int NewCount);

public sealed class FlashcardService(
    IAppDbContext db,
    IChatModel chat,
    AiUsageGuard usage,
    MasteryService mastery,
    TimeProvider clock)
{
    private readonly FsrsScheduler _fsrs = new();
    private const int NewCardsPerSession = 20;

    public async Task<PagedResult<FlashcardDto>> ListAsync(Guid userId, int? subjectId, Guid? bookId, string? state, string? search, int page, int pageSize, CancellationToken ct)
    {
        var now = Now();
        var query = db.Flashcards.AsNoTracking().Where(f => f.UserId == userId);
        if (subjectId is not null) query = query.Where(f => f.SubjectId == subjectId);
        if (bookId is not null) query = query.Where(f => f.BookId == bookId);
        query = state switch
        {
            "new" => query.Where(f => f.State == CardState.New),
            "due" => query.Where(f => f.State != CardState.New && f.Due <= now && !f.IsSuspended),
            "learning" => query.Where(f => f.State == CardState.Learning || f.State == CardState.Relearning),
            "mastered" => query.Where(f => f.State == CardState.Review && f.Stability >= 21),
            "suspended" => query.Where(f => f.IsSuspended),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(f => f.Front.Contains(search) || f.Back.Contains(search));

        var paged = await query.OrderBy(f => f.Due).ToPagedAsync(page, pageSize, ct);
        return new PagedResult<FlashcardDto>(paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    /// <summary>Cards to review now: due learning/review cards first, then a limited number of new cards.</summary>
    public async Task<DueQueueDto> GetDueAsync(Guid userId, int? subjectId, Guid? bookId, Guid? chapterId, int limit, CancellationToken ct)
    {
        var now = Now();
        var query = db.Flashcards.AsNoTracking().Where(f => f.UserId == userId && !f.IsSuspended);
        if (subjectId is not null) query = query.Where(f => f.SubjectId == subjectId);
        if (bookId is not null) query = query.Where(f => f.BookId == bookId);
        if (chapterId is not null) query = query.Where(f => f.ChapterId == chapterId);

        limit = Math.Clamp(limit, 1, 200);
        var dueCount = await query.CountAsync(f => f.State != CardState.New && f.Due <= now, ct);
        var newCount = await query.CountAsync(f => f.State == CardState.New, ct);
        var due = await query.Where(f => f.State != CardState.New && f.Due <= now).OrderBy(f => f.Due).Take(limit).ToListAsync(ct);
        var fresh = await query.Where(f => f.State == CardState.New).OrderBy(f => f.CreatedAt)
            .Take(Math.Min(NewCardsPerSession, Math.Max(0, limit - due.Count))).ToListAsync(ct);
        return new DueQueueDto(due.Concat(fresh).Select(ToDto).ToList(), dueCount, newCount);
    }

    public async Task<FlashcardDto> CreateAsync(Guid userId, CreateFlashcardRequest request, CancellationToken ct)
    {
        var card = await BuildAsync(userId, request, ct);
        db.Flashcards.Add(card);
        await db.SaveChangesAsync(ct);
        return ToDto(card);
    }

    public async Task<IReadOnlyList<FlashcardDto>> CreateBatchAsync(Guid userId, BatchCreateRequest request, CancellationToken ct)
    {
        if (request.Cards.Count is 0 or > 100) throw new ValidationException("invalid_batch", "Save between 1 and 100 cards at a time.");
        var cards = new List<Flashcard>();
        foreach (var c in request.Cards) cards.Add(await BuildAsync(userId, c, ct));
        db.Flashcards.AddRange(cards);
        await db.SaveChangesAsync(ct);
        return cards.Select(ToDto).ToList();
    }

    public async Task<FlashcardDto> UpdateAsync(Guid userId, Guid id, UpdateFlashcardRequest request, CancellationToken ct)
    {
        var card = await GetOwnedAsync(userId, id, ct);
        if (request.Front is not null) card.Front = Required(request.Front, 2000, "front");
        if (request.Back is not null) card.Back = Required(request.Back, 4000, "back");
        if (request.Tags is not null) card.Tags = NormalizeTags(request.Tags);
        if (request.IsSuspended is not null) card.IsSuspended = request.IsSuspended.Value;
        card.UpdatedAt = Now();
        await db.SaveChangesAsync(ct);
        return ToDto(card);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var card = await GetOwnedAsync(userId, id, ct);
        db.Flashcards.Remove(card);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IntervalPreviewDto> PreviewAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var card = ToFsrs(await GetOwnedAsync(userId, id, ct));
        var now = Now();
        double Days(ReviewRating r) => Math.Round(_fsrs.Review(card, r, now).ScheduledDays, 4);
        return new IntervalPreviewDto(Days(ReviewRating.Again), Days(ReviewRating.Hard), Days(ReviewRating.Good), Days(ReviewRating.Easy));
    }

    public async Task<ReviewResultDto> ReviewAsync(Guid userId, Guid id, ReviewRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Rating)) throw new ValidationException("invalid_rating", "Rating must be again, hard, good or easy.");
        var card = await GetOwnedAsync(userId, id, ct);
        var now = Now();
        var stateBefore = card.State;
        var result = _fsrs.Review(ToFsrs(card), request.Rating, now);

        card.State = result.Card.State;
        card.Due = result.Card.Due;
        card.Stability = result.Card.Stability;
        card.Difficulty = result.Card.Difficulty;
        card.Step = result.Card.Step;
        card.Reps = result.Card.Reps;
        card.Lapses = result.Card.Lapses;
        card.LastReview = now;
        card.UpdatedAt = now;

        db.FlashcardReviewLogs.Add(new FlashcardReviewLog
        {
            FlashcardId = card.Id, UserId = userId, SubjectId = card.SubjectId, Rating = request.Rating, StateBefore = stateBefore,
            StabilityAfter = card.Stability, DifficultyAfter = card.Difficulty, ElapsedDays = result.ElapsedDays,
            ScheduledDays = result.ScheduledDays, PredictedRecall = result.Retrievability, ReviewedAt = now,
            DurationMs = request.DurationMs is null ? null : Math.Clamp(request.DurationMs.Value, 0, 3_600_000)
        });

        // Only reviews of previously-learned cards say something about retention/mastery.
        if (stateBefore is CardState.Review or CardState.Relearning)
            await mastery.RecordAsync(userId, card.SubjectId, card.BookId, card.ChapterId, null, EvidenceKind.Card,
                request.Rating == ReviewRating.Again ? 0 : 1, card.SourcePage, ct);

        await db.SaveChangesAsync(ct);
        return new ReviewResultDto(ToDto(card), result.ScheduledDays, result.Retrievability);
    }

    public async Task<FlashcardStatsDto> GetStatsAsync(Guid userId, int? subjectId, CancellationToken ct)
    {
        var now = Now();
        var cards = db.Flashcards.AsNoTracking().Where(f => f.UserId == userId);
        var logs = db.FlashcardReviewLogs.AsNoTracking().Where(l => l.UserId == userId);
        if (subjectId is not null)
        {
            cards = cards.Where(f => f.SubjectId == subjectId);
            logs = logs.Where(l => l.SubjectId == subjectId);
        }

        var counts = await cards.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Due = g.Count(f => f.State != CardState.New && f.Due <= now && !f.IsSuspended),
            New = g.Count(f => f.State == CardState.New),
            Learning = g.Count(f => f.State == CardState.Learning || f.State == CardState.Relearning),
            Review = g.Count(f => f.State == CardState.Review),
            Mastered = g.Count(f => f.State == CardState.Review && f.Stability >= 21)
        }).FirstOrDefaultAsync(ct);

        var today = now.Date;
        var since30 = now.AddDays(-30);
        var recentLogs = await logs.Where(l => l.ReviewedAt >= since30).Select(l => new { l.ReviewedAt, l.Rating, l.StateBefore }).ToListAsync(ct);
        var matureReviews = recentLogs.Where(l => l.StateBefore == CardState.Review).ToList();
        double? retention = matureReviews.Count >= 5 ? Math.Round((double)matureReviews.Count(l => l.Rating != ReviewRating.Again) / matureReviews.Count, 3) : null;

        var reviewDays = await logs.Select(l => l.ReviewedAt.Date).Distinct().OrderByDescending(d => d).Take(400).ToListAsync(ct);
        var (streak, _) = StreakCalculator.Compute(reviewDays.Select(DateOnly.FromDateTime), DateOnly.FromDateTime(today));

        var horizon = now.Date.AddDays(7);
        var upcoming = await cards.Where(f => f.State != CardState.New && !f.IsSuspended && f.Due < horizon).Select(f => f.Due).ToListAsync(ct);
        var forecast = Enumerable.Range(0, 7).Select(i =>
        {
            var day = today.AddDays(i);
            var count = i == 0 ? upcoming.Count(d => d < day.AddDays(1)) : upcoming.Count(d => d >= day && d < day.AddDays(1));
            return new DailyCount(DateOnly.FromDateTime(day), count);
        }).ToList();

        return new FlashcardStatsDto(counts?.Total ?? 0, counts?.Due ?? 0, counts?.New ?? 0, counts?.Learning ?? 0, counts?.Review ?? 0,
            counts?.Mastered ?? 0, recentLogs.Count(l => l.ReviewedAt >= today), retention, streak, forecast);
    }

    /// <summary>AI-drafted cards from textbook content. Drafts are returned for the student to review before saving.</summary>
    public async Task<GeneratedFlashcardsDto> GenerateAsync(Guid userId, GenerateFlashcardsRequest request, CancellationToken ct)
    {
        await usage.EnsureAllowedAsync(userId, ct);
        var count = Math.Clamp(request.Count, 1, 30);
        SourceMaterial source;
        string material;
        HashSet<int> allowedPages;

        if (request.SummaryId is not null)
        {
            var summary = await db.Summaries.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SummaryId && s.UserId == userId, ct)
                          ?? throw new NotFoundException("Summary");
            source = await SummaryService.LoadSourceAsync(db, userId, summary.BookId, SummaryScope.Selection, summary.PageStart, null, null, null, summary.Content, ct);
            material = $"<selected_text>\n{Prompts.Escape(summary.Content)}\n</selected_text>";
            allowedPages = summary.Citations.SelectMany(c => Enumerable.Range(c.PageStart, c.PageEnd - c.PageStart + 1)).ToHashSet();
        }
        else
        {
            source = await SummaryService.LoadSourceAsync(db, userId, request.BookId, request.Scope, request.Page, request.PageStart, request.PageEnd, request.ChapterId, request.SelectedText, ct);
            if (source.SelectedText is not null)
            {
                material = $"<selected_text>\n{Prompts.Escape(source.SelectedText)}\n</selected_text>";
                allowedPages = source.PageStart is int p ? [p] : [];
            }
            else
            {
                var pages = TrimToBudget(source.Pages, 120_000, source.Warnings);
                material = Prompts.SourcePages(pages);
                allowedPages = pages.Select(x => x.Page).ToHashSet();
            }
        }

        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new UserPreferences();
        var language = request.Language is "ar" or "en" or "fr" ? request.Language : prefs.ExplanationLanguage;
        var focus = request.Focus switch
        {
            "definitions" => "Focus on definitions and key terms (term on the front, definition on the back).",
            "formulas" => "Focus on formulas and laws: the front asks for the formula or its meaning; the back gives it in LaTeX with symbols and units.",
            "vocabulary" => "Focus on vocabulary: the word or expression in the source language on the front; meaning, translation and an example sentence on the back.",
            _ => "Cover the most important facts, concepts, definitions and relationships."
        };

        var system = $"""
            You write high-quality flashcards for active recall from a high-school textbook.
            Write the cards in {Prompts.LanguageName(language)}, keeping important source-language terminology (in parentheses if translated).
            {focus}
            Rules: one idea per card; the front is a clear question or prompt; the back is a short, precise answer; no trivia; no duplicate cards;
            use only information in the source; set "page" to the source page the card comes from (or null if unknown); add 1–3 short topic tags.
            Math in LaTeX ($...$). Return JSON only.

            {Prompts.SecurityRules}
            """;

        ChatResult? result = null;
        try
        {
            result = await chat.CompleteAsync(new ChatRequest
            {
                Feature = "flashcards", SystemPrompt = system, MaxTokens = 8000, Effort = "low", JsonSchema = DraftSchema,
                Messages = [new ChatTurn(ChatRole.User, $"{material}\n\nCreate up to {count} flashcards.")]
            }, ct);
        }
        finally
        {
            usage.Record(userId, "flashcards", result, chat.ModelName, result is not null);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var parsed = AiJson.Parse<DraftEnvelope>(result.Text);
        var existing = await db.Flashcards.AsNoTracking().Where(f => f.UserId == userId && f.BookId == source.Book.Id)
            .Select(f => f.Front).ToListAsync(ct);
        var seen = existing.Select(AnswerGrader.Normalize).ToHashSet();

        var drafts = new List<FlashcardDraft>();
        foreach (var d in parsed.Cards ?? [])
        {
            var front = d.Front?.Trim() ?? "";
            var back = d.Back?.Trim() ?? "";
            if (front.Length is < 2 or > 2000 || back.Length is < 1 or > 4000) continue;
            if (!seen.Add(AnswerGrader.Normalize(front))) continue;
            var page = d.Page is int pg && allowedPages.Contains(pg) ? pg : (int?)null;
            drafts.Add(new FlashcardDraft(front, back, page, NormalizeTags(d.Tags ?? [])));
            if (drafts.Count >= count) break;
        }

        return new GeneratedFlashcardsDto(source.Book.SubjectId, source.Book.Id, source.Chapter?.Id, drafts, source.Warnings);
    }

    /// <summary>Turns the incorrect answers of a submitted quiz attempt into flashcards.</summary>
    public async Task<IReadOnlyList<FlashcardDto>> CreateFromQuizMistakesAsync(Guid userId, Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.QuizAttempts.AsNoTracking().Include(a => a.Quiz).ThenInclude(q => q!.Questions).Include(a => a.Responses)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && a.Status == AttemptStatus.Submitted, ct)
            ?? throw new NotFoundException("Attempt");
        var quiz = attempt.Quiz!;
        var wrongIds = attempt.Responses.Where(r => r.IsCorrect == false).Select(r => r.QuestionId).ToHashSet();
        var now = Now();

        var cards = quiz.Questions.Where(q => wrongIds.Contains(q.Id)).Select(q => new Flashcard
        {
            Id = Ids.New(), UserId = userId, SubjectId = quiz.SubjectId, BookId = quiz.BookId, ChapterId = q.ChapterId,
            Front = q.Prompt.Length > 2000 ? q.Prompt[..2000] : q.Prompt,
            Back = QuizAnswerText.Describe(q) is var answer && q.Explanation.Length > 0 ? Trim($"{answer}\n\n{q.Explanation}", 4000) : Trim(answer, 4000),
            Tags = q.Topic is null ? [] : [Trim(q.Topic, 40)], SourceType = ContentSourceType.QuizMistake, SourcePage = q.SourcePage,
            State = CardState.New, Due = now, CreatedAt = now, UpdatedAt = now
        }).ToList();

        db.Flashcards.AddRange(cards);
        await db.SaveChangesAsync(ct);
        return cards.Select(ToDto).ToList();
    }

    private async Task<Flashcard> BuildAsync(Guid userId, CreateFlashcardRequest request, CancellationToken ct)
    {
        var subjectId = request.SubjectId;
        if (request.BookId is not null)
        {
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
            subjectId ??= book.SubjectId;
            if (request.ChapterId is not null && !await db.Chapters.AnyAsync(c => c.Id == request.ChapterId && c.BookId == book.Id, ct))
                throw new NotFoundException("Chapter");
        }
        if (subjectId is null || !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw new ValidationException("invalid_subject", "A valid subject is required.");

        var now = Now();
        return new Flashcard
        {
            Id = Ids.New(), UserId = userId, SubjectId = subjectId.Value, BookId = request.BookId, ChapterId = request.ChapterId,
            Front = Required(request.Front, 2000, "front"), Back = Required(request.Back, 4000, "back"),
            Tags = NormalizeTags(request.Tags ?? []), SourceType = request.SourceType, SourcePage = request.SourcePage,
            State = CardState.New, Due = now, CreatedAt = now, UpdatedAt = now
        };
    }

    private static List<(int Page, string Text)> TrimToBudget(List<(int Page, string Text)> pages, int budget, List<string> warnings)
    {
        var result = new List<(int, string)>();
        var used = 0;
        foreach (var p in pages)
        {
            if (used + p.Text.Length > budget)
            {
                warnings.Add($"source_truncated|{result.LastOrDefault().Item1}");
                break;
            }
            result.Add(p);
            used += p.Text.Length;
        }
        return result;
    }

    private const string DraftSchema = """
        {"type":"object","properties":{"cards":{"type":"array","items":{"type":"object","properties":{
          "front":{"type":"string"},"back":{"type":"string"},"page":{"type":["integer","null"]},
          "tags":{"type":"array","items":{"type":"string"}}},
          "required":["front","back","page","tags"],"additionalProperties":false}}},
         "required":["cards"],"additionalProperties":false}
        """;

    private sealed record DraftEnvelope(List<DraftItem>? Cards);

    private sealed record DraftItem(string? Front, string? Back, int? Page, List<string>? Tags);

    private async Task<Flashcard> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.Flashcards.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, ct) ?? throw new NotFoundException("Flashcard");

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static FsrsCard ToFsrs(Flashcard f) => new(f.State, f.Due, f.Stability, f.Difficulty, f.Step, f.Reps, f.Lapses, f.LastReview);

    private static string Required(string value, int max, string field)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0 || v.Length > max) throw new ValidationException($"invalid_{field}", $"The {field} must be 1–{max} characters.");
        return v;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private static List<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();

    public static FlashcardDto ToDto(Flashcard f) => new(f.Id, f.SubjectId, f.BookId, f.ChapterId, f.Front, f.Back, f.Tags, f.SourceType, f.SourcePage,
        f.State, f.Due, Math.Round(f.Stability, 2), Math.Round(f.Difficulty, 2), f.Reps, f.Lapses, FsrsScheduler.IsMastered(f.State, f.Stability),
        f.IsSuspended, f.LastReview, f.CreatedAt);
}

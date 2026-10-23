using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Riwaq.Api.Infrastructure;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Flashcards;
using Riwaq.Application.Features.Notes;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Application.Features.Reader;
using Riwaq.Application.Features.Summaries;

namespace Riwaq.Api.Controllers;

[ApiController]
[Route("api/books/{bookId:guid}")]
public sealed class ReaderController(ReaderService reader) : ControllerBase
{
    [HttpGet("reader-state")]
    public Task<ReaderStateDto> State(Guid bookId, CancellationToken ct) => reader.GetStateAsync(User.GetUserId(), bookId, ct);

    [HttpPut("progress")]
    public Task<ReaderStateDto> Progress(Guid bookId, ProgressUpdateRequest request, CancellationToken ct) =>
        reader.UpdateProgressAsync(User.GetUserId(), bookId, request, ct);

    [HttpPut("chapters/{chapterId:guid}/status")]
    public async Task<IActionResult> ChapterStatus(Guid bookId, Guid chapterId, ChapterStatusRequest request, CancellationToken ct)
    {
        await reader.SetChapterStatusAsync(User.GetUserId(), bookId, chapterId, request, ct);
        return NoContent();
    }

    [HttpPost("bookmarks")]
    public Task<BookmarkDto> AddBookmark(Guid bookId, CreateBookmarkRequest request, CancellationToken ct) =>
        reader.AddBookmarkAsync(User.GetUserId(), bookId, request, ct);

    [HttpDelete("bookmarks/{id:guid}")]
    public async Task<IActionResult> DeleteBookmark(Guid bookId, Guid id, CancellationToken ct)
    {
        await reader.DeleteBookmarkAsync(User.GetUserId(), bookId, id, ct);
        return NoContent();
    }

    [HttpGet("highlights")]
    public Task<IReadOnlyList<HighlightDto>> Highlights(Guid bookId, [FromQuery] int? page, CancellationToken ct) =>
        reader.GetHighlightsAsync(User.GetUserId(), bookId, page, ct);

    [HttpPost("highlights")]
    public Task<HighlightDto> AddHighlight(Guid bookId, CreateHighlightRequest request, CancellationToken ct) =>
        reader.AddHighlightAsync(User.GetUserId(), bookId, request, ct);

    [HttpPatch("highlights/{id:guid}")]
    public Task<HighlightDto> UpdateHighlight(Guid bookId, Guid id, UpdateHighlightRequest request, CancellationToken ct) =>
        reader.UpdateHighlightAsync(User.GetUserId(), bookId, id, request, ct);

    [HttpDelete("highlights/{id:guid}")]
    public async Task<IActionResult> DeleteHighlight(Guid bookId, Guid id, CancellationToken ct)
    {
        await reader.DeleteHighlightAsync(User.GetUserId(), bookId, id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/summaries")]
public sealed class SummariesController(SummaryService summaries) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SummaryDto>> List([FromQuery] Guid? bookId, CancellationToken ct) => summaries.ListAsync(User.GetUserId(), bookId, ct);

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<SummaryDto> Generate(GenerateSummaryRequest request, CancellationToken ct) => summaries.GenerateAsync(User.GetUserId(), request, ct);

    [HttpGet("{id:guid}")]
    public Task<SummaryDto> Get(Guid id, CancellationToken ct) => summaries.GetAsync(User.GetUserId(), id, ct);

    [HttpPatch("{id:guid}")]
    public Task<SummaryDto> Update(Guid id, UpdateSummaryRequest request, CancellationToken ct) => summaries.UpdateAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await summaries.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/export")]
    [Produces("text/markdown")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var (fileName, markdown) = await summaries.ExportAsync(User.GetUserId(), id, ct);
        return File(Encoding.UTF8.GetBytes(markdown), "text/markdown; charset=utf-8", fileName);
    }
}

[ApiController]
[Route("api/flashcards")]
public sealed class FlashcardsController(FlashcardService cards) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<FlashcardDto>> List([FromQuery] int? subjectId, [FromQuery] Guid? bookId, [FromQuery] string? state, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        cards.ListAsync(User.GetUserId(), subjectId, bookId, state, search, page, pageSize, ct);

    [HttpGet("due")]
    public Task<DueQueueDto> Due([FromQuery] int? subjectId, [FromQuery] Guid? bookId, [FromQuery] Guid? chapterId, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        cards.GetDueAsync(User.GetUserId(), subjectId, bookId, chapterId, limit, ct);

    [HttpGet("stats")]
    public Task<FlashcardStatsDto> Stats([FromQuery] int? subjectId, CancellationToken ct) => cards.GetStatsAsync(User.GetUserId(), subjectId, ct);

    [HttpPost]
    public Task<FlashcardDto> Create(CreateFlashcardRequest request, CancellationToken ct) => cards.CreateAsync(User.GetUserId(), request, ct);

    [HttpPost("batch")]
    public Task<IReadOnlyList<FlashcardDto>> CreateBatch(BatchCreateRequest request, CancellationToken ct) => cards.CreateBatchAsync(User.GetUserId(), request, ct);

    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<GeneratedFlashcardsDto> Generate(GenerateFlashcardsRequest request, CancellationToken ct) => cards.GenerateAsync(User.GetUserId(), request, ct);

    [HttpPost("from-attempt/{attemptId:guid}")]
    public Task<IReadOnlyList<FlashcardDto>> FromAttempt(Guid attemptId, CancellationToken ct) => cards.CreateFromQuizMistakesAsync(User.GetUserId(), attemptId, ct);

    [HttpPut("{id:guid}")]
    public Task<FlashcardDto> Update(Guid id, UpdateFlashcardRequest request, CancellationToken ct) => cards.UpdateAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await cards.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/preview")]
    public Task<IntervalPreviewDto> Preview(Guid id, CancellationToken ct) => cards.PreviewAsync(User.GetUserId(), id, ct);

    [HttpPost("{id:guid}/review")]
    public Task<ReviewResultDto> Review(Guid id, ReviewRequest request, CancellationToken ct) => cards.ReviewAsync(User.GetUserId(), id, request, ct);
}

[ApiController]
[Route("api")]
public sealed class QuizzesController(QuizService quizzes) : ControllerBase
{
    [HttpGet("quizzes")]
    public Task<IReadOnlyList<QuizDto>> List([FromQuery] int? subjectId, [FromQuery] Guid? bookId, CancellationToken ct) =>
        quizzes.ListAsync(User.GetUserId(), subjectId, bookId, ct);

    [HttpPost("quizzes")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public Task<QuizDetailDto> Generate(GenerateQuizRequest request, CancellationToken ct) => quizzes.GenerateAsync(User.GetUserId(), request, ct);

    [HttpGet("quizzes/{id:guid}")]
    public Task<QuizDetailDto> Get(Guid id, CancellationToken ct) => quizzes.GetAsync(User.GetUserId(), id, ct);

    [HttpDelete("quizzes/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await quizzes.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("quizzes/{id:guid}/attempts")]
    public Task<AttemptDto> Start(Guid id, CancellationToken ct) => quizzes.StartAttemptAsync(User.GetUserId(), id, ct);

    [HttpGet("attempts")]
    public Task<IReadOnlyList<AttemptSummaryDto>> Attempts([FromQuery] int? subjectId, [FromQuery] Guid? quizId, CancellationToken ct) =>
        quizzes.ListAttemptsAsync(User.GetUserId(), subjectId, quizId, ct);

    [HttpGet("attempts/{attemptId:guid}")]
    public Task<AttemptDto> Attempt(Guid attemptId, CancellationToken ct) => quizzes.GetAttemptAsync(User.GetUserId(), attemptId, ct);

    [HttpPut("attempts/{attemptId:guid}/responses/{questionId:guid}")]
    public async Task<object> SaveResponse(Guid attemptId, Guid questionId, SaveResponseRequest request, CancellationToken ct)
    {
        var (response, reveal) = await quizzes.SaveResponseAsync(User.GetUserId(), attemptId, questionId, request, ct);
        return new { response, reveal };
    }

    [HttpPost("attempts/{attemptId:guid}/submit")]
    public Task<AttemptResultDto> Submit(Guid attemptId, CancellationToken ct) => quizzes.SubmitAsync(User.GetUserId(), attemptId, ct);

    [HttpGet("attempts/{attemptId:guid}/result")]
    public Task<AttemptResultDto> Result(Guid attemptId, CancellationToken ct) => quizzes.GetResultAsync(User.GetUserId(), attemptId, ct);

    [HttpPost("attempts/{attemptId:guid}/responses/{questionId:guid}/self-assess")]
    public Task<AttemptResultDto> SelfAssess(Guid attemptId, Guid questionId, SelfAssessRequest request, CancellationToken ct) =>
        quizzes.SelfAssessAsync(User.GetUserId(), attemptId, questionId, request, ct);
}

[ApiController]
[Route("api/notes")]
public sealed class NotesController(NotesService notes) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NoteDto>> List([FromQuery] int? subjectId, [FromQuery] Guid? bookId, [FromQuery] string? tag, [FromQuery] string? search,
        [FromQuery] bool? pinned, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        notes.ListAsync(User.GetUserId(), subjectId, bookId, tag, search, pinned, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<NoteDto> Get(Guid id, CancellationToken ct) => notes.GetAsync(User.GetUserId(), id, ct);

    [HttpPost]
    public Task<NoteDto> Create(SaveNoteRequest request, CancellationToken ct) => notes.CreateAsync(User.GetUserId(), request, ct);

    [HttpPost("from-message")]
    public Task<NoteDto> FromMessage(NoteFromMessageRequest request, CancellationToken ct) => notes.CreateFromMessageAsync(User.GetUserId(), request, ct);

    [HttpPut("{id:guid}")]
    public Task<NoteDto> Update(Guid id, SaveNoteRequest request, CancellationToken ct) => notes.UpdateAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await notes.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("export")]
    [Produces("text/markdown")]
    public async Task<IActionResult> Export([FromQuery] int? subjectId, [FromQuery] Guid? bookId, CancellationToken ct)
    {
        var (fileName, markdown) = await notes.ExportAsync(User.GetUserId(), subjectId, bookId, ct);
        return File(Encoding.UTF8.GetBytes(markdown), "text/markdown; charset=utf-8", fileName);
    }
}

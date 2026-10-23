using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Riwaq.Api.Infrastructure;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Books;

namespace Riwaq.Api.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksController(BooksService books) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BookDto>> List([FromQuery] int? subjectId, CancellationToken ct) =>
        books.ListAsync(User.GetUserId(), subjectId, ct);

    /// <summary>Uploads a PDF (multipart/form-data). Processing continues in the background.</summary>
    [HttpPost("upload")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(160L * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<BookDto>> Upload(
        IFormFile file,
        [FromForm] int subjectId,
        [FromForm] string? title,
        [FromForm] string? academicYear,
        [FromForm] string? semester,
        [FromForm] string? collection,
        [FromForm] string? tags,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file_empty", "No file was uploaded.");
        if (file.Length > books.MaxUploadBytes) throw new ValidationException("file_too_large", "The file is too large.");

        var tagList = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await using var stream = file.OpenReadStream();
        var book = await books.UploadAsync(User.GetUserId(), stream, file.FileName,
            new UploadBookRequest(subjectId, title, academicYear, semester, collection, tagList), ct);
        return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
    }

    [HttpGet("{id:guid}")]
    public Task<BookDetailDto> Get(Guid id, CancellationToken ct) => books.GetAsync(User.GetUserId(), id, ct);

    [HttpPatch("{id:guid}")]
    public Task<BookDto> Update(Guid id, UpdateBookRequest request, CancellationToken ct) =>
        books.UpdateAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await books.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/processing-status")]
    public Task<ProcessingStatusDto> Status(Guid id, CancellationToken ct) => books.GetStatusAsync(User.GetUserId(), id, ct);

    [HttpPost("{id:guid}/reprocess")]
    public Task<ProcessingStatusDto> Reprocess(Guid id, CancellationToken ct) => books.ReprocessAsync(User.GetUserId(), id, ct);

    [HttpGet("{id:guid}/chapters")]
    public Task<IReadOnlyList<ChapterDto>> Chapters(Guid id, CancellationToken ct) => books.GetChaptersAsync(User.GetUserId(), id, ct);

    [HttpPatch("{id:guid}/chapters/{chapterId:guid}")]
    public Task<ChapterDto> UpdateChapter(Guid id, Guid chapterId, UpdateChapterRequest request, CancellationToken ct) =>
        books.UpdateChapterAsync(User.GetUserId(), id, chapterId, request, ct);

    [HttpGet("{id:guid}/pages/{page:int}/text")]
    public Task<PageTextDto> PageText(Guid id, int page, CancellationToken ct) => books.GetPageTextAsync(User.GetUserId(), id, page, ct);

    /// <summary>Streams the original PDF to its owner. Supports range requests for progressive loading.</summary>
    [HttpGet("{id:guid}/file")]
    [Produces("application/pdf")]
    public async Task<IActionResult> File(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var (stream, _) = await books.OpenFileAsync(userId, id, ct);
        await books.MarkOpenedAsync(userId, id, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }
}

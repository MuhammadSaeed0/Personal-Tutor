using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Riwaq.Api.Infrastructure;
using Riwaq.Application.Features.Tutor;

namespace Riwaq.Api.Controllers;

[ApiController]
[Route("api/tutor/conversations")]
public sealed class TutorController(TutorService tutor, IOptions<JsonOptions> jsonOptions, ILogger<TutorController> logger) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ConversationDto>> List([FromQuery] Guid? bookId, [FromQuery] int? subjectId, CancellationToken ct) =>
        tutor.ListAsync(User.GetUserId(), bookId, subjectId, ct);

    [HttpPost]
    public Task<ConversationDto> Create(CreateConversationRequest request, CancellationToken ct) => tutor.CreateAsync(User.GetUserId(), request, ct);

    [HttpPatch("{id:guid}")]
    public Task<ConversationDto> Rename(Guid id, RenameConversationRequest request, CancellationToken ct) => tutor.RenameAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tutor.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages")]
    public Task<IReadOnlyList<MessageDto>> Messages(Guid id, CancellationToken ct) => tutor.GetMessagesAsync(User.GetUserId(), id, ct);

    /// <summary>
    /// Asks the tutor. The response is a Server-Sent Events stream with events
    /// <c>start</c>, <c>delta</c> (text chunks), then <c>done</c> (final message with citations) or <c>error</c>.
    /// Aborting the request stops generation; the partial answer is saved as "stopped".
    /// </summary>
    [HttpPost("{id:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    [Produces("text/event-stream")]
    public Task Ask(Guid id, AskRequest request, CancellationToken ct) =>
        StreamAsync(tutor.AskAsync(User.GetUserId(), id, request, ct), ct);

    [HttpPost("{id:guid}/messages/{messageId:guid}/retry")]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    [Produces("text/event-stream")]
    public Task Retry(Guid id, Guid messageId, CancellationToken ct) =>
        StreamAsync(tutor.RetryAsync(User.GetUserId(), id, messageId, ct), ct);

    [HttpPut("{id:guid}/messages/{messageId:guid}/feedback")]
    public Task<MessageDto> Feedback(Guid id, Guid messageId, FeedbackRequest request, CancellationToken ct) =>
        tutor.SetFeedbackAsync(User.GetUserId(), id, messageId, request, ct);

    private async Task StreamAsync(IAsyncEnumerable<TutorEvent> events, CancellationToken ct)
    {
        await using var enumerator = events.GetAsyncEnumerator(ct);

        // The first step runs validation, ownership and quota checks. If it throws, nothing has been
        // written yet and the normal problem-details handler produces the error response.
        var hasFirst = await enumerator.MoveNextAsync();

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        var json = jsonOptions.Value.JsonSerializerOptions;

        try
        {
            if (hasFirst)
            {
                await WriteAsync(enumerator.Current, json, ct);
                while (await enumerator.MoveNextAsync()) await WriteAsync(enumerator.Current, json, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // client stopped generation
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tutor stream failed");
            await WriteAsync(new TutorErrorEvent("server_error", "The answer could not be completed.", null), json, CancellationToken.None);
        }
    }

    private async Task WriteAsync(TutorEvent e, JsonSerializerOptions json, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(e, e.GetType(), json);
        await Response.WriteAsync($"event: {e.Type}\ndata: {payload}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}

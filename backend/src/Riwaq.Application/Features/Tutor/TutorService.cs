using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Ai;
using Riwaq.Application.Features.Retrieval;
using Riwaq.Application.Text;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Tutor;

public sealed record ConversationDto(Guid Id, Guid? BookId, int? SubjectId, string Title, DateTime CreatedAt, DateTime UpdatedAt, string? LastMessagePreview);

public sealed record MessageDto(
    Guid Id, MessageRole Role, string Content, MessageStatus Status, IReadOnlyList<SourceReference> Citations,
    IReadOnlyList<string> FollowUps, bool Grounded, int? ContextPage, string? SelectedText, string? Action,
    int? Feedback, string? ErrorCode, DateTime CreatedAt);

public sealed record CreateConversationRequest(Guid? BookId, int? SubjectId, string? Title);

public sealed record RenameConversationRequest(string Title);

public sealed record AskRequest(
    string? Question,
    string? Action = null,
    string? SelectedText = null,
    int? CurrentPage = null,
    IReadOnlyList<Guid>? BookIds = null,
    string? Language = null);

public sealed record FeedbackRequest(int? Value);

public abstract record TutorEvent(string Type);
public sealed record TutorStartEvent(MessageDto UserMessage, Guid AssistantMessageId, IReadOnlyList<Guid> SearchedBookIds) : TutorEvent("start");
public sealed record TutorDeltaEvent(string Text) : TutorEvent("delta");
public sealed record TutorDoneEvent(MessageDto Message) : TutorEvent("done");
public sealed record TutorErrorEvent(string Code, string Message, Guid? AssistantMessageId) : TutorEvent("error");

public sealed class TutorService(
    IAppDbContext db,
    IChatModel chat,
    RetrievalService retrieval,
    AiUsageGuard usage,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<TutorService> logger)
{
    private const int MaxQuestionChars = 4000;
    private const int MaxSelectionChars = 6000;
    private const int HistoryMessages = 10;
    private int TopK => configuration.GetValue("Retrieval:TopK", 8);

    // ---- Conversations ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ConversationDto>> ListAsync(Guid userId, Guid? bookId, int? subjectId, CancellationToken ct)
    {
        var query = db.Conversations.AsNoTracking().Where(c => c.UserId == userId);
        if (bookId is not null) query = query.Where(c => c.BookId == bookId);
        if (subjectId is not null) query = query.Where(c => c.SubjectId == subjectId);
        return await query.OrderByDescending(c => c.UpdatedAt).Take(100)
            .Select(c => new ConversationDto(c.Id, c.BookId, c.SubjectId, c.Title, c.CreatedAt, c.UpdatedAt,
                c.Messages.Where(m => m.Role == MessageRole.User).OrderByDescending(m => m.CreatedAt).Select(m => m.Content.Substring(0, Math.Min(120, m.Content.Length))).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<ConversationDto> CreateAsync(Guid userId, CreateConversationRequest request, CancellationToken ct)
    {
        int? subjectId = request.SubjectId;
        if (request.BookId is not null)
        {
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId && b.UserId == userId, ct)
                       ?? throw new NotFoundException("Book");
            subjectId = book.SubjectId;
        }
        else if (subjectId is not null && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
        {
            throw new ValidationException("invalid_subject", "Unknown subject.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var conversation = new Conversation
        {
            Id = Ids.New(), UserId = userId, BookId = request.BookId, SubjectId = subjectId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? string.Empty : Truncate(request.Title.Trim(), 200),
            TitleIsCustom = !string.IsNullOrWhiteSpace(request.Title), CreatedAt = now, UpdatedAt = now
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return new ConversationDto(conversation.Id, conversation.BookId, conversation.SubjectId, conversation.Title, now, now, null);
    }

    public async Task<ConversationDto> RenameAsync(Guid userId, Guid conversationId, RenameConversationRequest request, CancellationToken ct)
    {
        var conversation = await GetOwnedAsync(userId, conversationId, ct);
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) throw new ValidationException("invalid_title", "Title is required.");
        conversation.Title = Truncate(title, 200);
        conversation.TitleIsCustom = true;
        await db.SaveChangesAsync(ct);
        return new ConversationDto(conversation.Id, conversation.BookId, conversation.SubjectId, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, null);
    }

    public async Task DeleteAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var conversation = await GetOwnedAsync(userId, conversationId, ct);
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        await GetOwnedAsync(userId, conversationId, ct);
        var messages = await db.ConversationMessages.AsNoTracking().Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
        return messages.Select(ToDto).ToList();
    }

    public async Task<MessageDto> SetFeedbackAsync(Guid userId, Guid conversationId, Guid messageId, FeedbackRequest request, CancellationToken ct)
    {
        await GetOwnedAsync(userId, conversationId, ct);
        var message = await db.ConversationMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId && m.Role == MessageRole.Assistant, ct)
                      ?? throw new NotFoundException("Message");
        message.Feedback = request.Value is null ? null : Math.Sign(request.Value.Value);
        await db.SaveChangesAsync(ct);
        return ToDto(message);
    }

    // ---- Asking -------------------------------------------------------------------------------------

    public async IAsyncEnumerable<TutorEvent> AskAsync(Guid userId, Guid conversationId, AskRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0 && string.IsNullOrEmpty(request.Action))
            throw new ValidationException("empty_question", "Please type a question.");
        if (question.Length > MaxQuestionChars) throw new ValidationException("question_too_long", "The question is too long.");
        if (request.SelectedText?.Length > MaxSelectionChars) throw new ValidationException("selection_too_long", "The selected text is too long.");

        var conversation = await GetOwnedAsync(userId, conversationId, ct);
        await usage.EnsureAllowedAsync(userId, ct);

        var userMessage = new ConversationMessage
        {
            Id = Ids.New(), ConversationId = conversation.Id, Role = MessageRole.User,
            Content = question, Status = MessageStatus.Complete, ContextPage = request.CurrentPage,
            SelectedText = string.IsNullOrWhiteSpace(request.SelectedText) ? null : request.SelectedText.Trim(),
            Action = string.IsNullOrWhiteSpace(request.Action) ? null : request.Action, CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.ConversationMessages.Add(userMessage);
        await db.SaveChangesAsync(ct);

        await foreach (var e in GenerateAsync(userId, conversation, userMessage, request.BookIds, request.Language, ct))
            yield return e;
    }

    /// <summary>Regenerates the answer to the user message that precedes the given assistant message.</summary>
    public async IAsyncEnumerable<TutorEvent> RetryAsync(Guid userId, Guid conversationId, Guid assistantMessageId, [EnumeratorCancellation] CancellationToken ct)
    {
        var conversation = await GetOwnedAsync(userId, conversationId, ct);
        await usage.EnsureAllowedAsync(userId, ct);
        var assistant = await db.ConversationMessages.FirstOrDefaultAsync(m => m.Id == assistantMessageId && m.ConversationId == conversationId && m.Role == MessageRole.Assistant, ct)
                        ?? throw new NotFoundException("Message");
        var userMessage = await db.ConversationMessages
            .Where(m => m.ConversationId == conversationId && m.Role == MessageRole.User && m.CreatedAt <= assistant.CreatedAt)
            .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Message");

        db.ConversationMessages.Remove(assistant);
        await db.SaveChangesAsync(ct);

        await foreach (var e in GenerateAsync(userId, conversation, userMessage, null, null, ct))
            yield return e;
    }

    private async IAsyncEnumerable<TutorEvent> GenerateAsync(Guid userId, Conversation conversation, ConversationMessage userMessage,
        IReadOnlyList<Guid>? requestedBooks, string? languageOverride, [EnumeratorCancellation] CancellationToken ct)
    {
        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new UserPreferences();
        var language = languageOverride is "ar" or "en" or "fr" ? languageOverride : prefs.ExplanationLanguage;
        var subject = conversation.SubjectId is null ? null : await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == conversation.SubjectId, ct);
        var book = conversation.BookId is null ? null : await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == conversation.BookId && b.UserId == userId, ct);

        // Scope: explicit cross-book selection, else the conversation's book, else every book in the subject.
        var scope = await ResolveScopeAsync(userId, conversation, requestedBooks, ct);
        var crossBook = scope.Count > 1;

        var history = await BuildHistoryAsync(conversation.Id, userMessage, ct);
        var pages = PageReferenceParser.Parse(userMessage.Content).ToList();
        if (userMessage.ContextPage is int page && (userMessage.Action is "explain_page" || userMessage.SelectedText is not null || ReferencesCurrentPage(userMessage.Content)))
            pages.Add(page);

        var retrievalText = BuildRetrievalText(userMessage, history);
        var passages = scope.Count == 0
            ? []
            : await retrieval.SearchAsync(userId, new RetrievalQuery(scope, retrievalText, pages.Distinct().ToList(), null, TopK), ct);

        string? chapterTitle = null;
        if (book is not null && userMessage.ContextPage is int current)
        {
            chapterTitle = await db.Chapters.AsNoTracking()
                .Where(c => c.BookId == book.Id && c.StartPage <= current && c.EndPage >= current)
                .OrderByDescending(c => c.Level).Select(c => c.Title).FirstOrDefaultAsync(ct);
        }

        var request = new ChatRequest
        {
            Feature = "tutor",
            SystemPrompt = Prompts.Tutor(subject?.Code, subject?.NameEn ?? "General", book?.Title, language, prefs.BilingualTerms, prefs.TeachingStyle),
            Messages = [.. history, new ChatTurn(ChatRole.User, Prompts.TutorUserTurn(userMessage.Content, userMessage.Action, language,
                userMessage.SelectedText, userMessage.ContextPage, chapterTitle, passages, crossBook))],
            MaxTokens = 6000
        };

        var assistant = new ConversationMessage
        {
            Id = Ids.New(), ConversationId = conversation.Id, Role = MessageRole.Assistant, Status = MessageStatus.Streaming,
            Grounded = passages.Count > 0, Model = chat.ModelName, CreatedAt = clock.GetUtcNow().UtcDateTime.AddMilliseconds(1)
        };
        db.ConversationMessages.Add(assistant);
        await db.SaveChangesAsync(ct);
        yield return new TutorStartEvent(ToDto(userMessage), assistant.Id, scope);

        var text = new StringBuilder();
        ChatResult? final = null;
        AiUnavailableException? failure = null;
        var completed = false;

        var stream = chat.StreamAsync(request, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                ChatStreamChunk chunk;
                try
                {
                    if (!await stream.MoveNextAsync()) break;
                    chunk = stream.Current;
                }
                catch (AiUnavailableException ex)
                {
                    failure = ex;
                    break;
                }

                if (chunk.TextDelta is { } delta)
                {
                    text.Append(delta);
                    yield return new TutorDeltaEvent(delta);
                }
                if (chunk.Final is not null) final = chunk.Final;
            }

            if (failure is not null)
            {
                assistant.Status = MessageStatus.Error;
                assistant.ErrorCode = failure.Code;
                assistant.Content = text.ToString();
                usage.Record(userId, "tutor", null, chat.ModelName, success: false);
                await db.SaveChangesAsync(CancellationToken.None);
                completed = true;
                yield return new TutorErrorEvent(failure.Code, failure.Message, assistant.Id);
                yield break;
            }

            var processed = CitationProcessor.Process(text.ToString(), passages);
            assistant.Content = processed.Content;
            assistant.Citations = processed.Citations;
            assistant.FollowUps = processed.FollowUps;
            assistant.Status = MessageStatus.Complete;
            assistant.Model = final?.Model ?? chat.ModelName;
            conversation.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            if (!conversation.TitleIsCustom && string.IsNullOrEmpty(conversation.Title))
                conversation.Title = Truncate(DeriveTitle(userMessage), 80);
            usage.Record(userId, "tutor", final, chat.ModelName, success: true);
            await db.SaveChangesAsync(CancellationToken.None);
            completed = true;
            yield return new TutorDoneEvent(ToDto(assistant));
        }
        finally
        {
            await stream.DisposeAsync();
            if (!completed)
            {
                // The student pressed "stop" or the connection dropped: keep what was generated.
                try
                {
                    assistant.Status = MessageStatus.Stopped;
                    var processed = CitationProcessor.Process(text.ToString(), passages);
                    assistant.Content = processed.Content;
                    assistant.Citations = processed.Citations;
                    usage.Record(userId, "tutor", null, chat.ModelName, success: true);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not save stopped tutor message {MessageId}", assistant.Id);
                }
            }
        }
    }

    private async Task<List<Guid>> ResolveScopeAsync(Guid userId, Conversation conversation, IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        if (requested is { Count: > 0 })
        {
            var ids = requested.Distinct().Take(10).ToList();
            var owned = await db.Books.AsNoTracking().Where(b => b.UserId == userId && ids.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
            if (owned.Count != ids.Count) throw new NotFoundException("Book");
            return owned;
        }
        if (conversation.BookId is not null) return [conversation.BookId.Value];
        if (conversation.SubjectId is not null)
            return await db.Books.AsNoTracking().Where(b => b.UserId == userId && b.SubjectId == conversation.SubjectId).Select(b => b.Id).ToListAsync(ct);
        return [];
    }

    private async Task<List<ChatTurn>> BuildHistoryAsync(Guid conversationId, ConversationMessage current, CancellationToken ct)
    {
        var recent = await db.ConversationMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Id != current.Id && m.Status == MessageStatus.Complete)
            .OrderByDescending(m => m.CreatedAt).Take(HistoryMessages).ToListAsync(ct);
        recent.Reverse();

        var turns = new List<ChatTurn>();
        foreach (var m in recent)
        {
            var role = m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant;
            var content = m.Role == MessageRole.User && m.SelectedText is not null
                ? $"(About the highlighted text: \"{Truncate(m.SelectedText, 300)}\")\n{m.Content}"
                : m.Content;
            if (string.IsNullOrWhiteSpace(content)) content = m.Action ?? "…";
            // Keep strict user/assistant alternation starting with a user turn.
            if (turns.Count == 0 && role != ChatRole.User) continue;
            if (turns.Count > 0 && turns[^1].Role == role) turns[^1] = turns[^1] with { Content = turns[^1].Content + "\n\n" + content };
            else turns.Add(new ChatTurn(role, content));
        }
        if (turns.Count > 0 && turns[^1].Role == ChatRole.User) turns.RemoveAt(turns.Count - 1);
        return turns;
    }

    private static string BuildRetrievalText(ConversationMessage message, List<ChatTurn> history)
    {
        var sb = new StringBuilder(message.Content);
        if (message.SelectedText is not null) sb.Append(' ').Append(Truncate(message.SelectedText, 1500));
        // Short follow-ups ("why?", "give an example") are retrieved together with the previous question.
        if (message.Content.Length < 40 && message.SelectedText is null)
        {
            var previous = history.LastOrDefault(t => t.Role == ChatRole.User);
            if (previous is not null) sb.Append(' ').Append(Truncate(previous.Content, 500));
        }
        return sb.ToString();
    }

    private static bool ReferencesCurrentPage(string question)
    {
        var q = question.ToLowerInvariant();
        return q.Contains("this page") || q.Contains("cette page") || q.Contains("هذه الصفحة") || q.Contains("الصفحة الحالية")
               || q.Contains("this paragraph") || q.Contains("هذه الفقرة");
    }

    private static string DeriveTitle(ConversationMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content)) return message.Content.Replace('\n', ' ');
        if (message.SelectedText is not null) return message.SelectedText.Replace('\n', ' ');
        return message.Action ?? "Conversation";
    }

    private async Task<Conversation> GetOwnedAsync(Guid userId, Guid conversationId, CancellationToken ct) =>
        await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct) ?? throw new NotFoundException("Conversation");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    public static MessageDto ToDto(ConversationMessage m) => new(m.Id, m.Role, m.Content, m.Status, m.Citations, m.FollowUps, m.Grounded,
        m.ContextPage, m.SelectedText, m.Action, m.Feedback, m.ErrorCode, m.CreatedAt);
}

namespace Riwaq.Domain.Entities;

/// <summary>
/// A verified reference to textbook content. Page numbers and quotes always come from
/// stored chunks — never from model output — so citations cannot be fabricated.
/// </summary>
public class SourceReference
{
    public int Marker { get; set; }
    public Guid BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public long? ChunkId { get; set; }
    public string? ChapterTitle { get; set; }
    public int PageStart { get; set; }
    public int PageEnd { get; set; }
    public string? Quote { get; set; }
}

public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    public int? SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool TitleIsCustom { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}

public class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageStatus Status { get; set; }

    // Context the student explicitly supplied with a question.
    public int? ContextPage { get; set; }
    public string? SelectedText { get; set; }
    public string? Action { get; set; }

    public List<SourceReference> Citations { get; set; } = [];
    public List<string> FollowUps { get; set; } = [];

    /// <summary>True when the retrieval step found textbook passages for this answer.</summary>
    public bool Grounded { get; set; }
    public int? Feedback { get; set; }
    public string? Model { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GeneratedSummary
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public Guid? ChapterId { get; set; }
    public SummaryScope Scope { get; set; }
    public SummaryStyle Style { get; set; }
    public string Language { get; set; } = "ar";
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<SourceReference> Citations { get; set; } = [];

    /// <summary>Notes about incomplete extraction (scanned pages, truncated input) shown with the summary.</summary>
    public List<string> Warnings { get; set; } = [];
    public bool IsEdited { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Note
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int? SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public int? PageNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool IsPinned { get; set; }
    public ContentSourceType SourceType { get; set; }
    public List<SourceReference> Sources { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Flashcard
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public ContentSourceType SourceType { get; set; }
    public int? SourcePage { get; set; }
    public bool IsSuspended { get; set; }

    // FSRS scheduling state.
    public CardState State { get; set; } = CardState.New;
    public DateTime Due { get; set; }
    public double Stability { get; set; }
    public double Difficulty { get; set; }
    public int Step { get; set; }
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public DateTime? LastReview { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FlashcardReviewLog
{
    public long Id { get; set; }
    public Guid FlashcardId { get; set; }
    public Guid UserId { get; set; }
    public int SubjectId { get; set; }
    public ReviewRating Rating { get; set; }
    public CardState StateBefore { get; set; }
    public double StabilityAfter { get; set; }
    public double DifficultyAfter { get; set; }
    public double ElapsedDays { get; set; }
    public double ScheduledDays { get; set; }

    /// <summary>Retrievability predicted just before this review (for retention statistics).</summary>
    public double? PredictedRecall { get; set; }
    public DateTime ReviewedAt { get; set; }
    public int? DurationMs { get; set; }
}

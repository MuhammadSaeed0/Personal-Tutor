namespace Riwaq.Domain.Entities;

public class Quiz
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    public Guid? ChapterId { get; set; }
    public string Title { get; set; } = string.Empty;
    public QuizMode Mode { get; set; }
    public QuizStatus Status { get; set; }
    public Difficulty Difficulty { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public int? TimeLimitSeconds { get; set; }
    public bool HintsAllowed { get; set; }
    public bool RevealImmediately { get; set; }
    public bool FocusWeakAreas { get; set; }
    public string Language { get; set; } = "ar";
    public int ExcludedQuestionCount { get; set; }
    /// <summary>Error code when generation failed (never provider messages or source text).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Built from the student's Ministry exam template (marks per question, time); shown with a bubble sheet.</summary>
    public bool MinistryFormat { get; set; }

    /// <summary>The generation request, kept so a quiz written in the background survives restarts.</summary>
    public string? RequestJson { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<QuizQuestion> Questions { get; set; } = [];
}

public class QuizQuestion
{
    public Guid Id { get; set; }
    public Guid QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public int Ordinal { get; set; }
    public QuestionType Type { get; set; }
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Type-specific presentation data: choices, matching pairs, items to order.</summary>
    public string OptionsJson { get; set; } = "{}";

    /// <summary>Answer key / rubric. Never sent to the client before the answer is graded.</summary>
    public string AnswerKeyJson { get; set; } = "{}";
    public string Explanation { get; set; } = string.Empty;
    public string? Hint { get; set; }
    public Difficulty Difficulty { get; set; }
    public Guid? ChapterId { get; set; }
    public string? Topic { get; set; }
    public int? SourcePage { get; set; }
    public string? SourceQuote { get; set; }
    public double Points { get; set; } = 1;

    /// <summary>Optional diagram the question refers to: sanitised SVG markup and its text alternative.</summary>
    public string? DiagramSvg { get; set; }
    public string? DiagramAlt { get; set; }

    /// <summary>For a question copied into a review quiz: the question it was copied from (its mistake is updated).</summary>
    public Guid? OriginQuestionId { get; set; }
}

/// <summary>
/// A question the student got wrong, brought back on a widening schedule (1, 3, 7, 14 days) until it is answered
/// correctly twice in a row.
/// </summary>
public class MistakeEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public QuizQuestion? Question { get; set; }
    public int SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public int TimesWrong { get; set; }
    public int CorrectStreak { get; set; }

    /// <summary>Index into the review intervals: 0 = 1 day, 1 = 3 days, 2 = 7 days, 3 = 14 days.</summary>
    public int Stage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastWrongAt { get; set; }
    public DateTime NextReviewAt { get; set; }
    public DateTime? MasteredAt { get; set; }
}

public class QuizAttempt
{
    public Guid Id { get; set; }
    public Guid QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public Guid UserId { get; set; }
    public AttemptStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public double Score { get; set; }
    public double MaxScore { get; set; }
    public int DurationSeconds { get; set; }

    /// <summary>How many times the side chat assistant was asked during this attempt.</summary>
    public int AssistantUses { get; set; }
    public List<QuizResponse> Responses { get; set; } = [];
}

public class QuizResponse
{
    public Guid Id { get; set; }
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public string AnswerJson { get; set; } = "null";
    public bool MarkedForReview { get; set; }
    public bool? IsCorrect { get; set; }
    public double PointsAwarded { get; set; }
    public GradingMethod GradingMethod { get; set; } = GradingMethod.Pending;
    public string? Feedback { get; set; }
    public bool HintUsed { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A student's Ministry exam shape for one subject (missing = the Ministry default).</summary>
public class ExamTemplate
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SubjectId { get; set; }
    public int MultipleChoiceCount { get; set; }
    public int TwoMarkChoiceCount { get; set; }
    public int EssayCount { get; set; }
    public double EssayPoints { get; set; }
    public int DurationMinutes { get; set; }
    public int DiagramCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

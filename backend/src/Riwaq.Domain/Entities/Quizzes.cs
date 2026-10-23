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
    public string? ErrorMessage { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<QuizQuestion> Questions { get; set; } = [];
}

public class QuizQuestion
{
    public Guid Id { get; set; }
    public Guid QuizId { get; set; }
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

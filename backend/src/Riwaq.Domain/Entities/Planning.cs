namespace Riwaq.Domain.Entities;

public class StudyPlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int DailyMinutes { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<StudyTask> Tasks { get; set; } = [];
}

public class StudyTask
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public int Order { get; set; }
    public StudyTaskType Type { get; set; }
    public StudyTaskStatus Status { get; set; }
    public int? SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public string? Topic { get; set; }
    public int EstimatedMinutes { get; set; }

    /// <summary>Machine-readable reason code (e.g. "exam_soon", "weak_topic", "cards_due") localised by the UI.</summary>
    public string ReasonCode { get; set; } = string.Empty;
    public double Priority { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class StudySession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public StudySessionType Type { get; set; }
    public int? SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public string? Goal { get; set; }
    public string? Notes { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public int PlannedMinutes { get; set; }
}

/// <summary>Evidence-based mastery for one topic (a chapter or a labelled concept).</summary>
public class TopicMasteryRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SubjectId { get; set; }
    public Guid? BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public string TopicKey { get; set; } = string.Empty;
    public string TopicLabel { get; set; } = string.Empty;
    public int QuestionAttempts { get; set; }
    public int QuestionCorrect { get; set; }
    public int CardReviews { get; set; }
    public int CardLapses { get; set; }

    /// <summary>Recency-weighted estimate in [0,1]. Shown as "estimated", distinct from measured accuracy.</summary>
    public double EstimatedMastery { get; set; }
    public int? LastMistakePage { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Achievement
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Data { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiUsageRecord
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Feature { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedAt { get; set; }
}

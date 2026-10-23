namespace Riwaq.Domain;

public enum BookStatus
{
    Uploaded = 0,
    Processing = 1,
    Ready = 2,
    ReadyWithWarnings = 3,
    Failed = 4
}

/// <summary>Ordered stages of the ingestion pipeline. The numeric order is shown to the student as a timeline.</summary>
public enum ProcessingStage
{
    Queued = 0,
    Validating = 1,
    ExtractingText = 2,
    RecognizingScannedPages = 3,
    DetectingStructure = 4,
    ProcessingTablesAndFigures = 5,
    PreparingSearch = 6,
    GeneratingResources = 7,
    Completed = 8
}

public enum JobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

public enum ExtractionMethod
{
    None = 0,
    EmbeddedText = 1,
    Ocr = 2
}

public enum ChapterSource
{
    PdfOutline = 0,
    HeadingHeuristic = 1,
    User = 2
}

public enum WarningSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public enum ChapterStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    AlreadyKnown = 3
}

public enum MessageRole
{
    User = 0,
    Assistant = 1
}

public enum MessageStatus
{
    Complete = 0,
    Streaming = 1,
    Stopped = 2,
    Error = 3
}

public enum SummaryScope
{
    Selection = 0,
    Page = 1,
    PageRange = 2,
    Chapter = 3,
    Book = 4
}

public enum SummaryStyle
{
    Quick = 0,
    Simple = 1,
    Detailed = 2,
    KeyPoints = 3,
    Definitions = 4,
    Formulas = 5,
    Comparisons = 6,
    CausesEffects = 7,
    ExamQuestions = 8,
    RevisionSheet = 9
}

public enum ContentSourceType
{
    Manual = 0,
    Selection = 1,
    AiExplanation = 2,
    Summary = 3,
    QuizMistake = 4,
    Note = 5,
    AiGenerated = 6
}

/// <summary>FSRS card states.</summary>
public enum CardState
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3
}

public enum ReviewRating
{
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4
}

public enum QuizMode
{
    Practice = 0,
    Exam = 1
}

public enum QuizStatus
{
    Generating = 0,
    Ready = 1,
    Failed = 2
}

public enum QuestionType
{
    MultipleChoice = 0,
    TrueFalse = 1,
    FillBlank = 2,
    ShortAnswer = 3,
    Matching = 4,
    Ordering = 5,
    Numerical = 6,
    Structured = 7
}

public enum Difficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

public enum AttemptStatus
{
    InProgress = 0,
    Submitted = 1
}

public enum GradingMethod
{
    Deterministic = 0,
    AiRubric = 1,
    SelfAssessed = 2,
    Pending = 3
}

public enum StudyTaskType
{
    Read = 0,
    ReviewFlashcards = 1,
    PracticeQuiz = 2,
    ReviseWeakTopic = 3,
    ExamPreparation = 4
}

public enum StudyTaskStatus
{
    Pending = 0,
    Completed = 1,
    Skipped = 2
}

public enum StudySessionType
{
    Focus = 0,
    Reading = 1
}

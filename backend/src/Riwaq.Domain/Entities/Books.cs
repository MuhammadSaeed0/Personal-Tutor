namespace Riwaq.Domain.Entities;

public class Book
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public SourceKind Kind { get; set; } = SourceKind.Pdf;

    public string Title { get; set; } = string.Empty;
    public string? AcademicYear { get; set; }
    public string? Semester { get; set; }
    public string? Collection { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>Dominant script/language detected during extraction ("ar", "en", "fr", "mixed").</summary>
    public string? DetectedLanguage { get; set; }

    public BookStatus Status { get; set; } = BookStatus.Uploaded;
    public int PageCount { get; set; }

    // Video sources: the original link, the YouTube video id, channel and length.
    public string? SourceUrl { get; set; }
    public string? ExternalId { get; set; }
    public string? Author { get; set; }
    public int? DurationSeconds { get; set; }

    // File metadata. Files live in file storage, never in SQL Server: the PDF, the photos
    // ("{StorageKey}/{page}.jpg") or the downloaded captions of a video.
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Incremented on every reprocess so caches keyed on the book can be invalidated.</summary>
    public int ContentVersion { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastOpenedAt { get; set; }

    public List<Chapter> Chapters { get; set; } = [];
}

public class DocumentProcessingJob
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Book? Book { get; set; }
    public Guid UserId { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Queued;
    public ProcessingStage Stage { get; set; } = ProcessingStage.Queued;

    /// <summary>0–100 progress within the whole pipeline.</summary>
    public int Progress { get; set; }
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Retry back-off: a queued job is not picked up before this time.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>User-safe error description (never contains secrets or stack traces).</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Text recognised from a scanned page, saved as soon as the page is read so an interrupted or repeated run never
/// reads (and pays for) the same page twice. Kept across reprocessing; removed with the book.
/// </summary>
public class RecognizedPage
{
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DocumentPage
{
    public long Id { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public int CharCount { get; set; }
    public ExtractionMethod ExtractionMethod { get; set; }
    public bool IsScanned { get; set; }
    public int ImageCount { get; set; }
    public bool HasMathNotation { get; set; }
    public bool HasTableLikeLayout { get; set; }

    /// <summary>Video sources: the time span of the transcript segment this page holds.</summary>
    public int? StartSeconds { get; set; }
    public int? EndSeconds { get; set; }
}

/// <summary>A node in the book's structure tree. Level 1 = chapter, Level 2+ = sections.</summary>
public class Chapter
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>The title as detected, kept when the student corrects it.</summary>
    public string DetectedTitle { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Order { get; set; }
    public int StartPage { get; set; }
    public int EndPage { get; set; }
    public ChapterSource Source { get; set; }
}

public class DocumentChunk
{
    public long Id { get; set; }
    public Guid BookId { get; set; }
    public Guid? ChapterId { get; set; }
    public int Ordinal { get; set; }
    public int PageStart { get; set; }
    public int PageEnd { get; set; }
    public string? SectionTitle { get; set; }
    public string Text { get; set; } = string.Empty;
    public int TokenEstimate { get; set; }
    public ExtractionMethod ExtractionMethod { get; set; }

    /// <summary>SHA-256 of the normalised text. Lets reprocessing reuse existing embeddings.</summary>
    public string ContentHash { get; set; } = string.Empty;
}

/// <summary>An embedding vector for a chunk, stored as little-endian float32 bytes.</summary>
public class ChunkEmbedding
{
    public long Id { get; set; }
    public long ChunkId { get; set; }
    public Guid BookId { get; set; }
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public byte[] Vector { get; set; } = [];
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DocumentWarning
{
    public long Id { get; set; }
    public Guid BookId { get; set; }
    public int? PageNumber { get; set; }

    /// <summary>Machine-readable code (e.g. "scanned_page_no_ocr") localised by the frontend.</summary>
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public WarningSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }
}

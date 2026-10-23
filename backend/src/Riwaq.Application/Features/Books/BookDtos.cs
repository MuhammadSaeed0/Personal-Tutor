using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Books;

public sealed record BookDto(
    Guid Id,
    int SubjectId,
    string Title,
    string? AcademicYear,
    string? Semester,
    string? Collection,
    IReadOnlyList<string> Tags,
    BookStatus Status,
    int PageCount,
    string? DetectedLanguage,
    long FileSizeBytes,
    string OriginalFileName,
    DateTime CreatedAt,
    DateTime? LastOpenedAt,
    int? LastPage,
    double ReadingProgress,
    ProcessingStatusDto? Processing,
    SourceKind Kind,
    string? SourceUrl,
    string? ExternalId,
    string? Author,
    int? DurationSeconds);

public sealed record ProcessingStatusDto(
    Guid BookId,
    Guid? JobId,
    BookStatus BookStatus,
    JobStatus? JobStatus,
    ProcessingStage Stage,
    int Progress,
    int Attempt,
    string? ErrorCode,
    DateTime? UpdatedAt);

public sealed record ChapterDto(Guid Id, Guid? ParentId, string Title, string DetectedTitle, int Level, int Order, int StartPage, int EndPage, ChapterSource Source, ChapterStatus Status);

public sealed record WarningDto(long Id, int? PageNumber, string Code, string Message, WarningSeverity Severity);

public sealed record PageInfoDto(int PageNumber, int CharCount, ExtractionMethod ExtractionMethod, bool IsScanned, int ImageCount, bool HasMath, bool HasTable,
    int? StartSeconds, int? EndSeconds);

public sealed record PageTextDto(int PageNumber, string Text, ExtractionMethod ExtractionMethod, bool IsScanned, int? StartSeconds, int? EndSeconds);

public sealed record BookDetailDto(BookDto Book, IReadOnlyList<ChapterDto> Chapters, IReadOnlyList<WarningDto> Warnings, IReadOnlyList<PageInfoDto> Pages);

public sealed record UploadBookRequest(int SubjectId, string? Title, string? AcademicYear, string? Semester, string? Collection, IReadOnlyList<string>? Tags);

public sealed record AddVideoRequest(string Url, int SubjectId, string? Title, string? AcademicYear, string? Semester, string? Collection, IReadOnlyList<string>? Tags);

/// <summary>An uploaded file, independent of ASP.NET types.</summary>
public sealed record UploadedFile(string FileName, long Length, Func<Stream> Open);

public sealed record UpdateBookRequest(string? Title, int? SubjectId, string? AcademicYear, string? Semester, string? Collection, IReadOnlyList<string>? Tags);

public sealed record UpdateChapterRequest(string Title);

public static class BookMappings
{
    public static ProcessingStatusDto ToStatus(Book book, DocumentProcessingJob? job) => new(
        book.Id,
        job?.Id,
        book.Status,
        job?.Status,
        job?.Stage ?? (book.Status is BookStatus.Ready or BookStatus.ReadyWithWarnings ? ProcessingStage.Completed : ProcessingStage.Queued),
        job?.Progress ?? (book.Status is BookStatus.Ready or BookStatus.ReadyWithWarnings ? 100 : 0),
        job?.Attempt ?? 0,
        job?.Status == JobStatus.Failed ? job.ErrorMessage : null,
        job?.HeartbeatAt ?? job?.CreatedAt);

    public static BookDto ToDto(Book book, DocumentProcessingJob? job, ReadingProgress? progress) => new(
        book.Id, book.SubjectId, book.Title, book.AcademicYear, book.Semester, book.Collection, book.Tags,
        book.Status, book.PageCount, book.DetectedLanguage, book.FileSizeBytes, book.OriginalFileName,
        book.CreatedAt, book.LastOpenedAt, progress?.LastPage,
        book.PageCount > 0 && progress is not null ? Math.Round(100.0 * progress.ReadPageCount / book.PageCount, 1) : 0,
        job is null ? null : ToStatus(book, job),
        book.Kind, book.SourceUrl, book.ExternalId, book.Author, book.DurationSeconds);
}

/// <summary>Where each kind of source keeps its files in storage.</summary>
public static class SourceFiles
{
    public static string PdfKey(Guid userId, Guid bookId) => $"books/{userId:N}/{bookId:N}.pdf";
    public static string ImagesKey(Guid userId, Guid bookId) => $"books/{userId:N}/{bookId:N}";
    public static string CaptionsKey(Guid userId, Guid bookId) => $"books/{userId:N}/{bookId:N}.captions.json";
    public static string ImageKey(Book book, int page) => $"{book.StorageKey}/{page:0000}.jpg";
    public static string TranscriptCacheKey(Book book) => book.StorageKey.Replace(".captions.json", ".transcript.json", StringComparison.Ordinal);

    public static IEnumerable<string> AllKeys(Book book) => book.Kind switch
    {
        SourceKind.Images => Enumerable.Range(1, book.PageCount).Select(n => ImageKey(book, n)),
        SourceKind.YouTube => [book.StorageKey, TranscriptCacheKey(book)],
        _ => [book.StorageKey]
    };
}

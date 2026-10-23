namespace Riwaq.Application.Abstractions;

public sealed record PdfValidationResult(bool IsValid, string? ErrorCode, int PageCount, bool IsEncrypted);

public sealed record ExtractedLine(string Text, double FontSize, double Top, bool IsBold);

public sealed record ExtractedPage(
    int Number,
    string Text,
    IReadOnlyList<ExtractedLine> Lines,
    int LetterCount,
    int ImageCount,
    double Width,
    double Height);

public sealed record OutlineEntry(string Title, int PageNumber, int Level);

public sealed record ExtractedDocument(int PageCount, IReadOnlyList<ExtractedPage> Pages, IReadOnlyList<OutlineEntry> Outline);

public interface IPdfExtractor
{
    PdfValidationResult Validate(string filePath);
    ExtractedDocument Extract(string filePath, CancellationToken cancellationToken);
}

public sealed record OcrResult(bool Success, string Text, double? MeanConfidence, string? ErrorCode);

public interface IOcrEngine
{
    bool IsAvailable { get; }

    /// <summary>"ai-vision", "tesseract" or "none".</summary>
    string Name { get; }
    Task<OcrResult> RecognizePageAsync(string pdfPath, int pageNumber, string languages, CancellationToken cancellationToken);
}

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>A local path for libraries that need one (PDF parsing, OCR). Downloads to a temp file for remote stores.</summary>
    Task<string> GetLocalPathAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Wakes the background worker when a new processing job is queued.</summary>
public interface IProcessingSignal
{
    void Notify();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Pushes real-time updates to a user's connected clients (SignalR).</summary>
public interface IRealtimeNotifier
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken = default);
}

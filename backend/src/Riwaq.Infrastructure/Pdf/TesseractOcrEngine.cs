using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Pdf;

public sealed class OcrOptions
{
    public const string Section = "Ocr";
    public bool Enabled { get; set; } = true;

    /// <summary>Path to the tesseract executable; "tesseract" uses PATH.</summary>
    public string TesseractPath { get; set; } = "tesseract";
    public int Dpi { get; set; } = 300;
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>"Auto" (Claude vision when an AI provider is configured, else Tesseract), "Ai" or "Tesseract".</summary>
    public string Engine { get; set; } = "Auto";

    /// <summary>Height in pixels of the page image sent to the vision model.</summary>
    public int AiImageHeight { get; set; } = 2000;
}

/// <summary>
/// OCR for scanned pages: renders the page with PDFium (PDFtoImage) and runs the Tesseract CLI.
/// When Tesseract is not installed the engine reports itself unavailable and the pipeline records
/// a warning for each scanned page instead of pretending the page was read.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly OcrOptions _options;
    private readonly ILogger<TesseractOcrEngine> _logger;
    private readonly Lazy<bool> _available;

    public TesseractOcrEngine(IOptions<OcrOptions> options, ILogger<TesseractOcrEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
        _available = new Lazy<bool>(Probe);
    }

    public bool IsAvailable => _options.Enabled && _available.Value;
    public string Name => "tesseract";

    public async Task<OcrResult> RecognizePageAsync(string pdfPath, int pageNumber, string languages, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return new OcrResult(false, string.Empty, null, "ocr_unavailable");

        var imagePath = Path.Combine(Path.GetTempPath(), $"riwaq-ocr-{Guid.NewGuid():N}.png");
        try
        {
            await using (var pdf = File.OpenRead(pdfPath))
            {
                Conversion.SavePng(imagePath, pdf, pageNumber - 1, leaveOpen: false, password: null,
                    options: new RenderOptions { Dpi = _options.Dpi });
            }

            var (exitCode, stdout) = await RunAsync(_options.TesseractPath, $"\"{imagePath}\" stdout -l {languages} --psm 3 tsv", cancellationToken);
            if (exitCode != 0) return new OcrResult(false, string.Empty, null, "ocr_failed");
            var (text, confidence) = ParseTsv(stdout);
            return new OcrResult(text.Length > 0, text, confidence, text.Length > 0 ? null : "ocr_no_text");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR failed for page {Page}", pageNumber);
            return new OcrResult(false, string.Empty, null, "ocr_failed");
        }
        finally
        {
            try { File.Delete(imagePath); } catch { /* temp file */ }
        }
    }

    /// <summary>Rebuilds text from Tesseract TSV (level 5 = word), keeping line and paragraph breaks.</summary>
    internal static (string Text, double? Confidence) ParseTsv(string tsv)
    {
        var sb = new StringBuilder();
        var confidences = new List<double>();
        string? lastLine = null, lastPar = null;
        foreach (var row in tsv.Split('\n').Skip(1))
        {
            var cols = row.Split('\t');
            if (cols.Length < 12 || cols[0] != "5") continue;
            var word = cols[11].Trim();
            if (word.Length == 0) continue;

            var par = $"{cols[2]}-{cols[3]}";
            var line = $"{par}-{cols[4]}";
            if (lastPar is not null && par != lastPar) sb.Append("\n\n");
            else if (lastLine is not null && line != lastLine) sb.Append('\n');
            else if (sb.Length > 0) sb.Append(' ');
            sb.Append(word);
            lastPar = par;
            lastLine = line;

            if (double.TryParse(cols[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf) && conf >= 0)
                confidences.Add(conf);
        }
        return (sb.ToString().Trim(), confidences.Count > 0 ? confidences.Average() : null);
    }

    private bool Probe()
    {
        try
        {
            var (code, output) = RunAsync(_options.TesseractPath, "--version", CancellationToken.None).GetAwaiter().GetResult();
            var ok = code == 0 && output.Contains("tesseract", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation(ok ? "Tesseract OCR available" : "Tesseract OCR not available; scanned pages will be flagged");
            return ok;
        }
        catch
        {
            _logger.LogInformation("Tesseract OCR not found; scanned pages will be flagged");
            return false;
        }
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string file, string args, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }
        var output = await stdout;
        await stderr;
        return (process.ExitCode, output.Length > 0 ? output : string.Empty);
    }
}

using System.Text;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Outline;

namespace Riwaq.Infrastructure.Pdf;

/// <summary>
/// Extracts embedded text, layout lines (with font sizes for heading detection), image counts and the
/// PDF outline using PdfPig. Includes heuristics for right-to-left (Arabic) text order and multi-column pages.
/// </summary>
public sealed class PdfPigExtractor : IPdfExtractor
{
    public PdfValidationResult Validate(string filePath)
    {
        try
        {
            using (var fs = File.OpenRead(filePath))
            {
                var header = new byte[1024];
                var read = fs.Read(header, 0, header.Length);
                if (Encoding.ASCII.GetString(header, 0, read).IndexOf("%PDF-", StringComparison.Ordinal) < 0)
                    return new PdfValidationResult(false, "pdf_invalid_header", 0, false);
            }

            using var document = PdfDocument.Open(filePath, new ParsingOptions { UseLenientParsing = true });
            if (document.NumberOfPages == 0) return new PdfValidationResult(false, "pdf_no_pages", 0, false);
            // Touch the first page to make sure the content can actually be parsed.
            _ = document.GetPage(1).Letters.Count;
            return new PdfValidationResult(true, null, document.NumberOfPages, document.IsEncrypted);
        }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
        {
            return new PdfValidationResult(false, "pdf_encrypted", 0, true);
        }
        catch (Exception)
        {
            return new PdfValidationResult(false, "pdf_unreadable", 0, false);
        }
    }

    public ExtractedDocument Extract(string filePath, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(filePath, new ParsingOptions { UseLenientParsing = true });
        var pages = new List<ExtractedPage>(document.NumberOfPages);

        for (var n = 1; n <= document.NumberOfPages; n++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                pages.Add(ExtractPage(document.GetPage(n)));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A broken page must not abort the whole book; it is reported as empty and flagged later.
                pages.Add(new ExtractedPage(n, string.Empty, [], 0, 0, 0, 0));
            }
        }

        return new ExtractedDocument(document.NumberOfPages, pages, ReadOutline(document));
    }

    private static ExtractedPage ExtractPage(Page page)
    {
        var letters = page.Letters;
        int imageCount;
        try { imageCount = page.GetImages().Count(); }
        catch { imageCount = 0; }

        if (letters.Count == 0)
            return new ExtractedPage(page.Number, string.Empty, [], 0, imageCount, page.Width, page.Height);

        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
        var pageIsRtl = words.Sum(w => w.Text.Count(ArabicText.IsArabicLetter)) > words.Sum(w => w.Text.Length) * 0.4;

        IReadOnlyList<TextBlock> blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        blocks = pageIsRtl ? OrderRtlBlocks(blocks) : UnsupervisedReadingOrderDetector.Instance.Get(blocks).ToList();

        var lines = new List<ExtractedLine>();
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            foreach (var line in block.TextLines)
            {
                var lineText = BuildLineText(line.Words);
                if (string.IsNullOrWhiteSpace(lineText)) continue;
                var fontSize = Median(line.Words.SelectMany(w => w.Letters).Select(l => l.PointSize));
                var bold = line.Words.SelectMany(w => w.Letters).Count(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true)
                           > line.Words.Sum(w => w.Letters.Count) / 2;
                lines.Add(new ExtractedLine(TextCleaner.CleanExtracted(lineText), fontSize, line.BoundingBox.Top, bold));
                text.Append(lineText).Append('\n');
            }
            text.Append('\n');
        }

        var invisible = letters.Count(l => l.RenderingMode == TextRenderingMode.Neither);
        return new ExtractedPage(page.Number, TextCleaner.CleanExtracted(text.ToString()), lines, letters.Count, imageCount, page.Width, page.Height,
            HasInvisibleTextLayer: invisible * 2 > letters.Count);
    }

    /// <summary>Joins words of a line in reading order, fixing visually-ordered Arabic glyph runs.</summary>
    private static string BuildLineText(IReadOnlyList<Word> words)
    {
        var arabic = words.Sum(w => w.Text.Count(ArabicText.IsArabicLetter));
        var total = Math.Max(1, words.Sum(w => w.Text.Length));
        var rtl = arabic > total * 0.4;

        if (!rtl)
            return string.Join(' ', words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text.Any(ArabicText.IsArabicLetter) ? FixWordDirection(w) : w.Text));

        var rightToLeft = words.OrderByDescending(w => w.BoundingBox.Right).Select(FixWordDirection).ToList();
        return RtlLineAssembler.Assemble(rightToLeft);
    }

    private static string FixWordDirection(Word word)
    {
        var text = word.Text;
        if (word.Letters.Count < 2 || !text.Any(ArabicText.IsArabicLetter)) return text;

        // In logical order the first Arabic letter is the rightmost one. If glyphs run left→right the
        // PDF stored them in visual order and the string must be reversed (keeping digit runs intact).
        var first = word.Letters[0].StartBaseLine.X;
        var last = word.Letters[^1].StartBaseLine.X;
        if (first <= last) return ReverseKeepingNumbers(text);
        return text;
    }

    private static string ReverseKeepingNumbers(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool? inNumber = null;
        foreach (var c in s)
        {
            var isNum = char.IsDigit(c) || c is '.' or ',' && sb.Length > 0 && inNumber == true;
            if (inNumber is not null && isNum != inNumber)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
            inNumber = isNum;
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());

        var result = new StringBuilder(s.Length);
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Length > 0 && char.IsDigit(t[0])) result.Append(t);
            else for (var j = t.Length - 1; j >= 0; j--) result.Append(t[j]);
        }
        return result.ToString();
    }

    /// <summary>Right-to-left column ordering: columns from right to left, blocks top to bottom.</summary>
    private static List<TextBlock> OrderRtlBlocks(IReadOnlyList<TextBlock> blocks)
    {
        var columns = new List<List<TextBlock>>();
        foreach (var block in blocks.OrderByDescending(b => b.BoundingBox.Right))
        {
            var column = columns.FirstOrDefault(c => c.Any(other => HorizontalOverlap(block, other) > 0.5));
            if (column is null) columns.Add([block]);
            else column.Add(block);
        }
        return columns
            .OrderByDescending(c => c.Max(b => b.BoundingBox.Right))
            .SelectMany(c => c.OrderByDescending(b => b.BoundingBox.Top))
            .ToList();
    }

    private static double HorizontalOverlap(TextBlock a, TextBlock b)
    {
        var overlap = Math.Min(a.BoundingBox.Right, b.BoundingBox.Right) - Math.Max(a.BoundingBox.Left, b.BoundingBox.Left);
        var narrower = Math.Min(a.BoundingBox.Width, b.BoundingBox.Width);
        return narrower <= 0 ? 0 : overlap / narrower;
    }

    private static List<OutlineEntry> ReadOutline(PdfDocument document)
    {
        var entries = new List<OutlineEntry>();
        try
        {
            if (!document.TryGetBookmarks(out var bookmarks)) return entries;
            void Walk(IEnumerable<BookmarkNode> nodes, int level)
            {
                foreach (var node in nodes)
                {
                    if (node is DocumentBookmarkNode doc && doc.PageNumber > 0 && !string.IsNullOrWhiteSpace(node.Title))
                        entries.Add(new OutlineEntry(TextCleaner.CleanExtracted(node.Title), doc.PageNumber, level));
                    Walk(node.Children, level + 1);
                }
            }
            Walk(bookmarks.Roots, 1);
        }
        catch
        {
            // A corrupt outline is not fatal; heading heuristics are used instead.
        }
        return entries;
    }

    private static double Median(IEnumerable<double> values)
    {
        var list = values.Order().ToList();
        return list.Count == 0 ? 0 : list[list.Count / 2];
    }
}

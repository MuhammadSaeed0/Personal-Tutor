using System.Text.RegularExpressions;
using Riwaq.Application.Abstractions;
using Riwaq.Domain;

namespace Riwaq.Application.Ingestion;

public sealed record DetectedSection(string Title, int StartPage, int EndPage, int Level, ChapterSource Source);

/// <summary>
/// Detects chapters and sections. Prefers the PDF's own outline (bookmarks); otherwise uses heading
/// heuristics (font size, chapter keywords, repetition filtering). Returns nothing rather than inventing
/// a structure when the evidence is weak.
/// </summary>
public static partial class StructureDetector
{
    public const int MaxSections = 120;

    public static List<DetectedSection> Detect(ExtractedDocument document)
    {
        var fromOutline = FromOutline(document.Outline, document.PageCount);
        if (fromOutline.Count > 0) return fromOutline;
        return FromHeadings(document.Pages, document.PageCount);
    }

    private static List<DetectedSection> FromOutline(IReadOnlyList<OutlineEntry> outline, int pageCount)
    {
        var entries = outline
            .Where(e => e.Level <= 2 && e.PageNumber >= 1 && e.PageNumber <= pageCount && e.Title.Length is > 0 and <= 300)
            .OrderBy(e => e.PageNumber).ThenBy(e => e.Level)
            .Take(MaxSections)
            .ToList();
        if (entries.Count == 0) return [];

        // If the top level holds a single entry (e.g. the book title), promote level 2.
        if (entries.Count(e => e.Level == 1) == 1 && entries.Count(e => e.Level == 2) >= 2)
            entries = entries.Where(e => e.Level == 2).Select(e => e with { Level = 1 }).ToList();

        return BuildRanges(entries.Select(e => (e.Title, e.PageNumber, e.Level)).ToList(), pageCount, ChapterSource.PdfOutline);
    }

    private static List<DetectedSection> FromHeadings(IReadOnlyList<ExtractedPage> pages, int pageCount)
    {
        var allLines = pages.SelectMany(p => p.Lines.Select(l => (Page: p, Line: l))).ToList();
        if (allLines.Count == 0) return [];

        var bodySize = WeightedMedianFontSize(allLines.Select(x => x.Line));
        if (bodySize <= 0) return [];

        // Text repeated on many pages is a running header/footer, not a heading.
        var repeats = allLines
            .GroupBy(x => Normalize(x.Line.Text))
            .Where(g => g.Select(x => x.Page.Number).Distinct().Count() > 3)
            .Select(g => g.Key)
            .ToHashSet();

        var candidates = new List<(string Title, int Page, int Level, double Size)>();
        foreach (var page in pages)
        {
            foreach (var line in page.Lines.Take(12)) // headings sit near the top of a page
            {
                var text = line.Text.Trim();
                if (text.Length is < 3 or > 120 || repeats.Contains(Normalize(text))) continue;
                if (text.Count(char.IsLetter) < text.Length * 0.5) continue;

                var keywordLevel = KeywordLevel(text);
                var larger = line.FontSize >= bodySize * 1.3;
                var emphasised = line.IsBold && line.FontSize >= bodySize * 1.1;

                if (keywordLevel > 0 && (larger || emphasised))
                    candidates.Add((text, page.Number, keywordLevel, line.FontSize));
                else if (line.FontSize >= bodySize * 1.6 && !text.EndsWith('.'))
                    candidates.Add((text, page.Number, 0, line.FontSize));
            }
        }

        List<(string Title, int Page, int Level)> chosen;
        var keyworded = candidates.Where(c => c.Level > 0).ToList();
        if (keyworded.Count >= 2)
        {
            chosen = keyworded.GroupBy(c => c.Page).Select(g => g.First()).Select(c => (c.Title, c.Page, c.Level)).ToList();
        }
        else
        {
            // No chapter keywords: use the largest heading size that appears on a plausible number of pages.
            var tiers = candidates.Where(c => c.Level == 0)
                .GroupBy(c => Math.Round(c.Size))
                .OrderByDescending(g => g.Key)
                .FirstOrDefault(g => g.Select(c => c.Page).Distinct().Count() is var n && n >= 2 && n <= Math.Max(2, pageCount / 2));
            if (tiers is null) return [];
            chosen = tiers.GroupBy(c => c.Page).Select(g => g.First()).Select(c => (c.Title, c.Page, 1)).ToList();
        }

        return BuildRanges(chosen.OrderBy(c => c.Page).Take(MaxSections).ToList(), pageCount, ChapterSource.HeadingHeuristic);
    }

    /// <summary>
    /// Headings in text recognised from scanned pages (no font information): chapter/lesson keyword lines or Markdown
    /// headings near the top of a page. Pages listing several chapters are a table of contents and are skipped.
    /// </summary>
    public static List<DetectedSection> FromRecognisedText(IReadOnlyDictionary<int, string> pages, int pageCount)
    {
        var keyworded = new List<(string Title, int Page, int Level)>();
        var marked = new List<(string Title, int Page, int Level)>();
        foreach (var (number, text) in pages.OrderBy(p => p.Key))
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Count(l => KeywordLevel(l.TrimStart('#', ' ')) == 1) >= 3) continue;

            foreach (var raw in lines.Take(6))
            {
                var hashes = raw.TakeWhile(c => c == '#').Count();
                var title = raw.TrimStart('#', ' ').Trim();
                if (title.Length is < 3 or > 120 || title.StartsWith('[') || title.EndsWith('.')) continue;

                var level = KeywordLevel(title);
                if (level > 0) { keyworded.Add((title, number, level)); break; }
                if (hashes is 1 or 2) { marked.Add((title, number, hashes)); break; }
            }
        }

        var chosen = keyworded.Count >= 2 ? keyworded
            : marked.Count >= 2 && marked.Count <= Math.Max(2, pageCount / 2) ? marked
            : [];
        return BuildRanges(chosen.Take(MaxSections).ToList(), pageCount, ChapterSource.HeadingHeuristic);
    }

    private static List<DetectedSection> BuildRanges(List<(string Title, int Page, int Level)> entries, int pageCount, ChapterSource source)
    {
        var result = new List<DetectedSection>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var (title, start, level) = entries[i];
            var end = pageCount;
            for (var j = i + 1; j < entries.Count; j++)
            {
                if (entries[j].Level <= level)
                {
                    end = Math.Max(start, entries[j].Page - (entries[j].Page > start ? 1 : 0));
                    break;
                }
            }
            result.Add(new DetectedSection(title, start, end, level, source));
        }
        return result;
    }

    /// <summary>1 = unit/chapter keyword, 2 = lesson/section keyword, 0 = none.</summary>
    internal static int KeywordLevel(string text)
    {
        if (ChapterKeyword().IsMatch(text)) return 1;
        if (LessonKeyword().IsMatch(text)) return 2;
        return 0;
    }

    private static double WeightedMedianFontSize(IEnumerable<ExtractedLine> lines)
    {
        var weighted = lines.Where(l => l.FontSize > 0).OrderBy(l => l.FontSize).Select(l => (l.FontSize, Weight: l.Text.Length)).ToList();
        var total = weighted.Sum(w => w.Weight);
        var acc = 0;
        foreach (var (size, weight) in weighted)
        {
            acc += weight;
            if (acc >= total / 2) return size;
        }
        return 0;
    }

    private static string Normalize(string text) => new(text.Where(char.IsLetter).ToArray());

    [GeneratedRegex(@"^\s*(الفصل|الوحدة|الباب|الجزء|chapter|unit|part|chapitre|unité|partie)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterKeyword();

    [GeneratedRegex(@"^\s*(الدرس|المحور|الموضوع|lesson|section|leçon|séquence)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LessonKeyword();
}

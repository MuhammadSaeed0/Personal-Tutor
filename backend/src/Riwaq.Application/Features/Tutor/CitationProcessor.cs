using System.Text.RegularExpressions;
using Riwaq.Application.Features.Retrieval;
using Riwaq.Application.Text;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Tutor;

public sealed record ProcessedAnswer(string Content, List<SourceReference> Citations, List<string> FollowUps);

/// <summary>
/// Post-processes a model answer: extracts follow-up questions and validates citation markers against
/// the passages actually supplied. Markers pointing at passages that don't exist are removed, so a
/// citation shown to the student always maps to a real stored chunk and page range.
/// </summary>
public static partial class CitationProcessor
{
    public static ProcessedAnswer Process(string raw, IReadOnlyList<RetrievedPassage> passages)
    {
        var (body, followUps) = ExtractFollowUps(raw);
        var used = new SortedSet<int>();

        var content = MarkerGroup().Replace(body, match =>
        {
            var numbers = Numbers().Matches(ArabicText.NormalizeDigits(match.Groups[1].Value))
                .Select(m => int.Parse(m.Value))
                .Where(n => n >= 1 && n <= passages.Count)
                .Distinct()
                .ToList();
            foreach (var n in numbers) used.Add(n);
            return string.Concat(numbers.Select(n => $"[{n}]"));
        });

        var citations = used.Select(n =>
        {
            var p = passages[n - 1];
            return new SourceReference
            {
                Marker = n,
                BookId = p.BookId,
                BookTitle = p.BookTitle,
                ChunkId = p.ChunkId,
                ChapterTitle = p.ChapterTitle,
                PageStart = p.PageStart,
                PageEnd = p.PageEnd,
                Quote = Excerpt(p.Text)
            };
        }).ToList();

        return new ProcessedAnswer(content.Trim(), citations, followUps);
    }

    public static (string Body, List<string> FollowUps) ExtractFollowUps(string raw)
    {
        var start = raw.IndexOf("<followups>", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return (raw, []);
        var end = raw.IndexOf("</followups>", start, StringComparison.OrdinalIgnoreCase);
        var block = end < 0 ? raw[(start + 11)..] : raw[(start + 11)..end];
        var followUps = block.Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '•', ' ').Trim())
            .Select(l => LeadingNumber().Replace(l, ""))
            .Where(l => l.Length is > 2 and < 300)
            .Take(3)
            .ToList();
        return (raw[..start].TrimEnd(), followUps);
    }

    private static string Excerpt(string text)
    {
        var t = text.Replace('\n', ' ').Trim();
        return t.Length <= 280 ? t : t[..280].TrimEnd() + "…";
    }

    // [1], [1, 3], [١]، [2][3] — numbers only, so links like [text](url) are untouched.
    [GeneratedRegex(@"\[((?:[0-9٠-٩]{1,2})(?:\s*[,،]\s*[0-9٠-٩]{1,2})*)\](?!\()")]
    private static partial Regex MarkerGroup();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    [GeneratedRegex(@"^\d+[.)]\s*")]
    private static partial Regex LeadingNumber();
}

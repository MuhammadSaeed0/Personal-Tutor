using System.Text.RegularExpressions;

namespace Riwaq.Application.Text;

/// <summary>Finds explicit page references in a question, e.g. "explain page 42" or "اشرح صفحة ٤٢".</summary>
public static partial class PageReferenceParser
{
    public static IReadOnlyList<int> Parse(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return [];
        var text = ArabicText.NormalizeDigits(question);
        var pages = new SortedSet<int>();

        foreach (Match m in RangePattern().Matches(text))
        {
            var from = int.Parse(m.Groups["from"].Value);
            var to = int.Parse(m.Groups["to"].Value);
            if (to >= from && to - from <= 10)
                for (var p = from; p <= to; p++) pages.Add(p);
        }

        foreach (Match m in SinglePattern().Matches(text))
        {
            if (int.TryParse(m.Groups["page"].Value, out var p) && p > 0 && p < 100_000) pages.Add(p);
        }

        return pages.ToList();
    }

    [GeneratedRegex(@"(?:pages?|pp?\.|صفحات|الصفحات|الصفحة|صفحة|ص\.?)\s*(?<from>\d{1,5})\s*(?:-|–|to|à|إلى|الى|حتى)\s*(?<to>\d{1,5})", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"(?:\bpages?|\bp\.|الصفحة|صفحة|صفحه|\bص\.?)\s*(?:رقم|n°|no\.?|number)?\s*(?<page>\d{1,5})", RegexOptions.IgnoreCase)]
    private static partial Regex SinglePattern();
}

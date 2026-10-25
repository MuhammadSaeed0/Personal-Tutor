using System.Text;
using System.Text.RegularExpressions;

namespace Riwaq.Application.Text;

/// <summary>
/// Offline clean-up of another tool's OCR text layer, with no AI calls. It removes symbols and letter runs that carry
/// no meaning, and corrects a misread Arabic word only when the book itself makes the fix obvious: the word occurs once,
/// and a word used many times in the same book differs from it by one interior letter (two in long words). Anything
/// ambiguous is left as it is: a wrong "correction" would change the meaning, an uncorrected typo rarely does.
/// </summary>
public static partial class OcrTextRepair
{
    /// <summary>Times a word must occur in the book before it is trusted as a correction.</summary>
    private const int TrustedCount = 5;

    /// <summary>A run of at least this many short, uncommon Arabic fragments is OCR noise.</summary>
    private const int NoiseRunLength = 4;

    public sealed class Vocabulary
    {
        internal readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, Dictionary<string, int>> Surfaces = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, List<string>> Patterns = new(StringComparer.Ordinal);

        internal int Count(string folded) => Counts.GetValueOrDefault(folded);

        internal string Surface(string folded) => Surfaces[folded].MaxBy(s => s.Value).Key;
    }

    public static Vocabulary BuildVocabulary(IEnumerable<string> pages)
    {
        var vocabulary = new Vocabulary();
        foreach (var page in pages)
        foreach (Match match in Token().Matches(page))
        {
            var core = Core(match.Value, out _, out _);
            if (!IsArabicWord(core)) continue;
            var folded = ArabicText.FoldForMatching(core);
            vocabulary.Counts[folded] = vocabulary.Count(folded) + 1;
            if (!vocabulary.Surfaces.TryGetValue(folded, out var surfaces)) vocabulary.Surfaces[folded] = surfaces = new(StringComparer.Ordinal);
            surfaces[core] = surfaces.GetValueOrDefault(core) + 1;
        }

        foreach (var (folded, count) in vocabulary.Counts)
        {
            if (count < TrustedCount || folded.Length < 4) continue;
            foreach (var pattern in Wildcards(folded))
            {
                if (!vocabulary.Patterns.TryGetValue(pattern, out var words)) vocabulary.Patterns[pattern] = words = [];
                words.Add(folded);
            }
        }
        return vocabulary;
    }

    public static string Repair(string page, Vocabulary vocabulary)
    {
        // Direction marks scattered through the layer split words and mean nothing on their own.
        var source = page.Replace("‎", "").Replace("‏", "").Replace("؜", "");
        var tokens = Token().Matches(source).Select(m => (m.Index, m.Length, Text: m.Value)).ToList();
        var replacements = new string?[tokens.Count];

        for (var i = 0; i < tokens.Count; i++)
        {
            var core = Core(tokens[i].Text, out var before, out var after);
            var text = tokens[i].Text;
            if (text.Any(c => JunkSymbols.Contains(c)) && text.All(c => JunkSymbols.Contains(c) || Punctuation.Contains(c))) replacements[i] = string.Empty;
            else if (LatinJunk().IsMatch(core)) replacements[i] = string.Empty;
            else if (Correct(core, vocabulary) is { } fixedWord) replacements[i] = before + fixedWord + after;
        }

        // Runs of short fragments that are not words this book uses ("د سي ري رو رمس رمم").
        for (var start = 0; start < tokens.Count;)
        {
            var end = start;
            while (end < tokens.Count && replacements[end] is null && IsNoiseFragment(tokens[end].Text, vocabulary)) end++;
            if (end - start >= NoiseRunLength)
                for (var k = start; k < end; k++) replacements[k] = string.Empty;
            start = Math.Max(end, start + 1);
        }

        var result = new StringBuilder(source.Length);
        var position = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            result.Append(source, position, tokens[i].Index - position).Append(replacements[i] ?? tokens[i].Text);
            position = tokens[i].Index + tokens[i].Length;
        }
        result.Append(source, position, source.Length - position);
        return TextCleaner.CleanExtracted(result.ToString());
    }

    private static string? Correct(string core, Vocabulary vocabulary)
    {
        if (!IsArabicWord(core)) return null;
        var folded = ArabicText.FoldForMatching(core);
        if (folded.Length < 4 || vocabulary.Count(folded) > 1) return null;

        var candidates = Wildcards(folded)
            .SelectMany(p => vocabulary.Patterns.GetValueOrDefault(p) ?? [])
            .Where(c => c != folded)
            .Distinct()
            .Select(c => (Word: c, Differences: Differences(folded, c), Count: vocabulary.Count(c)))
            .OrderBy(c => c.Differences).ThenByDescending(c => c.Count)
            .ToList();
        if (candidates.Count == 0) return null;

        // Two plausible fixes: the book does not say which one was meant.
        var best = candidates[0];
        if (candidates.Count > 1 && candidates[1].Differences == best.Differences && candidates[1].Count * 2 > best.Count) return null;
        return vocabulary.Surface(best.Word);
    }

    /// <summary>
    /// The word with one interior letter (two, in words of seven letters or more) replaced by '?'. The first letter and
    /// the last two are never wildcards: that is where Arabic marks person, gender and number (يكتب/تكتب, معلمون/معلمين).
    /// </summary>
    private static IEnumerable<string> Wildcards(string word)
    {
        var interior = Enumerable.Range(1, Math.Max(0, word.Length - 3)).ToList();
        foreach (var i in interior) yield return Mask(word, i);
        if (word.Length < 7) yield break;
        foreach (var i in interior)
        foreach (var j in interior.Where(j => j > i))
            yield return Mask(Mask(word, i), j);
    }

    private static string Mask(string word, int index) => string.Concat(word.AsSpan(0, index), "?", word.AsSpan(index + 1));

    private static int Differences(string a, string b) => a.Zip(b).Count(p => p.First != p.Second);

    private static bool IsNoiseFragment(string token, Vocabulary vocabulary)
    {
        var core = Core(token, out _, out _);
        if (!IsArabicWord(core)) return false;
        var folded = ArabicText.FoldForMatching(core);
        return folded.Length <= 3 && vocabulary.Count(folded) < TrustedCount;
    }

    private static bool IsArabicWord(string core) =>
        core.Length > 0 && core.All(c => ArabicText.IsArabicLetter(c) && !char.IsDigit(c) && c is not ('،' or '؛' or '؟'));

    /// <summary>The token without leading and trailing punctuation, which is returned separately.</summary>
    private static string Core(string token, out string before, out string after)
    {
        var start = 0;
        var end = token.Length;
        while (start < end && Punctuation.Contains(token[start])) start++;
        while (end > start && Punctuation.Contains(token[end - 1])) end--;
        before = token[..start];
        after = token[end..];
        return token[start..end];
    }

    private const string Punctuation = ".,:;!?()[]{}«»\"'“”‘’،؛؟-–—…*";

    /// <summary>Symbols other OCR tools put where a scan has a bullet, a frame or a smudge.</summary>
    private const string JunkSymbols = "©®|¦";

    [GeneratedRegex(@"\S+")]
    private static partial Regex Token();

    /// <summary>Latin letters glued to brackets, underscores or backslashes ("dla_c]"): never a word or a formula.</summary>
    [GeneratedRegex(@"^(?=.*[A-Za-z])(?=.*[_\[\]\\|]).{1,10}$")]
    private static partial Regex LatinJunk();
}

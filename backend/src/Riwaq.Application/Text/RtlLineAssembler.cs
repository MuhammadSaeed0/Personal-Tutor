using System.Globalization;

namespace Riwaq.Application.Text;

/// <summary>
/// Rebuilds the logical text of a right-to-left line from words given in right-to-left visual order.
/// Fixes the three artefacts PDF text commonly has in Arabic lines:
/// stray diacritics extracted as separate "words", mirrored brackets, and embedded left-to-right runs
/// (Latin words, symbols) whose word order and trailing punctuation came out reversed.
/// </summary>
public static class RtlLineAssembler
{
    private const string Punctuation = ".,،؛:;!?";

    public static string Assemble(IReadOnlyList<string> wordsRightToLeft)
    {
        var words = wordsRightToLeft
            .Where(w => !IsOnlyMarks(w))
            .Select(FixMirroredBrackets)
            .ToList();

        var result = new List<string>(words.Count);
        var i = 0;
        while (i < words.Count)
        {
            if (!IsLeftToRight(words[i]))
            {
                result.Add(words[i++]);
                continue;
            }

            // A run of left-to-right words was emitted right-to-left: restore its order.
            var run = new List<string>();
            while (i < words.Count && IsLeftToRight(words[i])) run.Add(words[i++]);
            run.Reverse();

            // Sentence punctuation drawn at the run's visual left edge belongs after the run.
            var leading = new string(run[0].TakeWhile(c => Punctuation.Contains(c)).ToArray());
            if (leading.Length > 0 && leading.Length < run[0].Length)
            {
                run[0] = run[0][leading.Length..];
                run[^1] += leading;
            }
            result.AddRange(run);
        }
        return string.Join(' ', result);
    }

    /// <summary>Diacritics (harakat) positioned apart from their letters are dropped rather than attached to the wrong letter.</summary>
    internal static bool IsOnlyMarks(string word) =>
        word.Length > 0 && word.All(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c == 'ـ');

    private static bool IsLeftToRight(string word) =>
        !word.Any(ArabicText.IsArabicLetter) && word.Any(char.IsLetter);

    /// <summary>In RTL text a "(" is drawn as ")". If a word's first bracket is a closing one, the glyphs are mirrored.</summary>
    internal static string FixMirroredBrackets(string word)
    {
        var firstBracket = word.FirstOrDefault(c => c is '(' or ')' or '[' or ']' or '{' or '}' or '«' or '»');
        if (firstBracket is not (')' or ']' or '}' or '»')) return word;
        return new string(word.Select(c => c switch
        {
            '(' => ')', ')' => '(', '[' => ']', ']' => '[', '{' => '}', '}' => '{', '«' => '»', '»' => '«', _ => c
        }).ToArray());
    }
}

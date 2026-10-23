using System.Text;
using System.Text.RegularExpressions;

namespace Riwaq.Application.Text;

/// <summary>
/// Cleans text extracted from PDFs while preserving scientific notation.
/// Full NFKC normalisation is deliberately avoided: it would turn "m²" into "m2" and "H₂O" into "H2O".
/// </summary>
public static partial class TextCleaner
{
    public static string CleanExtracted(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (ArabicText.IsPresentationForm(c) || c is >= 'ﬀ' and <= 'ﬆ')
            {
                // Arabic presentation forms and Latin ligatures (ﬁ) → standard characters.
                sb.Append(c.ToString().Normalize(NormalizationForm.FormKC));
            }
            else if (c == ' ' || c == ' ' || c == ' ')
            {
                sb.Append(' ');
            }
            else if (c is '​' or '﻿' or '­')
            {
                // zero-width space, BOM, soft hyphen
            }
            else if (char.IsControl(c) && c is not '\n' and not '\t')
            {
                if (c == '\r') continue;
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        var text = sb.ToString();
        text = HyphenatedLineBreak().Replace(text, "$1$2");
        text = MultiSpace().Replace(text, " ");
        text = SpaceAroundNewline().Replace(text, "\n");
        text = ManyNewlines().Replace(text, "\n\n");
        return text.Trim();
    }

    /// <summary>Heuristic: does the text contain equations, formulas or scientific notation?</summary>
    public static bool LooksMathematical(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var mathChars = 0;
        foreach (var c in text)
        {
            if (c is '=' or '±' or '×' or '÷' or '√' or '∑' or '∫' or '∆' or 'Δ' or '→' or '⇌' or '≈' or '≤' or '≥' or 'π' or 'λ' or 'Ω' or 'μ'
                || c is >= '⁰' and <= '₟') // super/subscripts
                mathChars++;
        }
        return mathChars >= 3 || Equation().IsMatch(text);
    }

    /// <summary>Heuristic for table-like layout: many short lines with several wide gaps.</summary>
    public static bool LooksTabular(IEnumerable<string> lines)
    {
        var tabularLines = lines.Count(l => WideGaps().Matches(l).Count >= 2);
        return tabularLines >= 4;
    }

    [GeneratedRegex(@"([A-Za-zÀ-ÿ])-\n([a-zà-ÿ])")]
    private static partial Regex HyphenatedLineBreak();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex SpaceAroundNewline();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();

    [GeneratedRegex(@"[A-Za-z]\s*=\s*[\dA-Za-z(]")]
    private static partial Regex Equation();

    [GeneratedRegex(@"\S\s{3,}\S")]
    private static partial Regex WideGaps();
}

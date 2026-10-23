using System.Globalization;
using System.Text;

namespace Riwaq.Application.Text;

/// <summary>Arabic-aware normalisation helpers.</summary>
public static class ArabicText
{
    /// <summary>
    /// Folds a string for matching only (search, answer comparison): removes diacritics and tatweel,
    /// unifies alef/ya/ta-marbuta variants, converts Arabic-Indic digits, lower-cases Latin text.
    /// Never use the result as displayable content.
    /// </summary>
    public static string FoldForMatching(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var raw in input.Normalize(NormalizationForm.FormD))
        {
            var c = raw;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue; // Latin accents and Arabic harakat
            if (c is 'ـ') continue;                          // tatweel
            if (c is >= 'ً' and <= 'ٟ' or 'ٰ') continue;
            c = c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => c
            };
            c = ToAsciiDigit(c);
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static char ToAsciiDigit(char c) => c switch
    {
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // Arabic-Indic
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // Extended (Persian)
        _ => c
    };

    public static string NormalizeDigits(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++) chars[i] = ToAsciiDigit(chars[i]);
        return new string(chars);
    }

    public static bool IsArabicLetter(char c) =>
        c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿';

    public static bool IsPresentationForm(char c) => c is >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿';
}

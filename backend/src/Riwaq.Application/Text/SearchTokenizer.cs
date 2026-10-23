namespace Riwaq.Application.Text;

/// <summary>Language-light tokenizer for keyword retrieval over Arabic, English and French text.</summary>
public static class SearchTokenizer
{
    private static readonly HashSet<string> StopWords =
    [
        // Arabic (already folded: أ→ا, ى→ي, ة→ه)
        "في", "من", "الي", "علي", "عن", "ان", "هذا", "هذه", "ذلك", "تلك", "التي", "الذي", "الذين", "ما", "ماذا", "لماذا", "كيف",
        "هل", "او", "ثم", "مع", "كان", "كانت", "يكون", "هو", "هي", "هم", "لا", "لم", "لن", "قد", "كل", "بين", "عند", "حتي", "اذا",
        "و", "ف", "ب", "ل", "ك", "اشرح", "وضح", "ما هو", "ص", "صفحه",
        // English
        "the", "a", "an", "of", "to", "in", "on", "and", "or", "is", "are", "was", "were", "be", "for", "with", "this", "that",
        "what", "why", "how", "does", "do", "it", "as", "by", "at", "from", "explain", "page", "please",
        // French
        "le", "la", "les", "un", "une", "des", "de", "du", "et", "ou", "est", "sont", "en", "dans", "pour", "par", "que", "qui", "au", "aux"
    ];

    private static readonly string[] ArabicPrefixes = ["وال", "بال", "كال", "فال", "لل", "ال"];

    public static List<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        var folded = ArabicText.FoldForMatching(text);
        var start = -1;
        for (var i = 0; i <= folded.Length; i++)
        {
            var isWordChar = i < folded.Length && (char.IsLetterOrDigit(folded[i]));
            if (isWordChar && start < 0) start = i;
            else if (!isWordChar && start >= 0)
            {
                AddToken(tokens, folded[start..i]);
                start = -1;
            }
        }
        return tokens;
    }

    private static void AddToken(List<string> tokens, string token)
    {
        if (token.Length == 0 || StopWords.Contains(token)) return;
        var stemmed = Stem(token);
        if (stemmed.Length < 2 && !char.IsDigit(stemmed[0])) return;
        tokens.Add(stemmed);
    }

    /// <summary>Very light stemming: strips the Arabic definite article and simple Latin plurals.</summary>
    public static string Stem(string token)
    {
        if (ArabicText.IsArabicLetter(token[0]))
        {
            foreach (var prefix in ArabicPrefixes)
            {
                if (token.Length - prefix.Length >= 2 && token.StartsWith(prefix, StringComparison.Ordinal))
                    return token[prefix.Length..];
            }
            return token;
        }

        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal)) return token[..^3] + "y";
        if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal)) return token[..^1];
        return token;
    }
}

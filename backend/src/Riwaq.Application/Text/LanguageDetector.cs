namespace Riwaq.Application.Text;

public static class LanguageDetector
{
    private static readonly string[] FrenchMarkers = [" le ", " la ", " les ", " des ", " est ", " une ", " dans ", " pour ", " du ", " et "];
    private static readonly string[] EnglishMarkers = [" the ", " and ", " is ", " of ", " to ", " in ", " that ", " with "];

    /// <summary>Returns "ar", "en", "fr", "mixed" or null when there is too little text.</summary>
    public static string? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int arabic = 0, latin = 0;
        foreach (var c in text)
        {
            if (ArabicText.IsArabicLetter(c)) arabic++;
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'À' and <= 'ÿ') latin++;
        }
        var letters = arabic + latin;
        if (letters < 50) return null;
        if (arabic > letters * 0.7) return "ar";
        if (latin > letters * 0.7)
        {
            var sample = " " + text.ToLowerInvariant() + " ";
            var fr = FrenchMarkers.Sum(m => Count(sample, m));
            var en = EnglishMarkers.Sum(m => Count(sample, m));
            return fr > en ? "fr" : "en";
        }
        return "mixed";
    }

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}

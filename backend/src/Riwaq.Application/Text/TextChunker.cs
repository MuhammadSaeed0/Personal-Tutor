using System.Security.Cryptography;
using System.Text;
using Riwaq.Domain;

namespace Riwaq.Application.Text;

public sealed record PageText(int PageNumber, string Text, ExtractionMethod Method);

public sealed record ChapterRange(Guid Id, string Title, int StartPage, int EndPage);

public sealed record TextChunk(
    int Ordinal,
    int PageStart,
    int PageEnd,
    Guid? ChapterId,
    string? SectionTitle,
    string Text,
    ExtractionMethod Method,
    string ContentHash);

/// <summary>
/// Splits page text into retrieval-friendly chunks. Chunks follow paragraph boundaries,
/// never cross chapter boundaries, and keep the exact page range they came from.
/// </summary>
public static class TextChunker
{
    public static List<TextChunk> Chunk(
        IReadOnlyList<PageText> pages,
        IReadOnlyList<ChapterRange> chapters,
        int targetChars = 1200,
        int maxChars = 1800)
    {
        var chunks = new List<TextChunk>();
        var buffer = new StringBuilder();
        int? bufferStart = null, bufferEnd = null;
        ChapterRange? bufferChapter = null;
        var bufferMethod = ExtractionMethod.EmbeddedText;

        void Flush()
        {
            var text = buffer.ToString().Trim();
            if (text.Length >= 20 && bufferStart is not null)
            {
                chunks.Add(new TextChunk(chunks.Count, bufferStart.Value, bufferEnd!.Value, bufferChapter?.Id,
                    bufferChapter?.Title, text, bufferMethod, Hash(text)));
            }
            buffer.Clear();
            bufferStart = bufferEnd = null;
        }

        foreach (var page in pages.OrderBy(p => p.PageNumber))
        {
            if (string.IsNullOrWhiteSpace(page.Text)) continue;
            var chapter = FindChapter(chapters, page.PageNumber);
            if (bufferChapter?.Id != chapter?.Id) Flush();
            bufferChapter = chapter;

            foreach (var paragraph in SplitParagraphs(page.Text, maxChars))
            {
                if (buffer.Length > 0 && buffer.Length + paragraph.Length > targetChars) Flush();
                if (buffer.Length > 0) buffer.Append("\n\n");
                buffer.Append(paragraph);
                bufferStart ??= page.PageNumber;
                bufferEnd = page.PageNumber;
                bufferMethod = page.Method;
            }
        }
        Flush();
        return chunks;
    }

    private static ChapterRange? FindChapter(IReadOnlyList<ChapterRange> chapters, int page)
    {
        ChapterRange? best = null;
        foreach (var c in chapters)
        {
            if (page < c.StartPage || page > c.EndPage) continue;
            // Prefer the most specific (shortest) range containing the page.
            if (best is null || c.EndPage - c.StartPage < best.EndPage - best.StartPage) best = c;
        }
        return best;
    }

    /// <summary>Splits on blank lines; very long paragraphs are split on sentence ends.</summary>
    public static IEnumerable<string> SplitParagraphs(string text, int maxChars)
    {
        foreach (var raw in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length <= maxChars)
            {
                yield return raw;
                continue;
            }

            var sb = new StringBuilder();
            foreach (var sentence in SplitSentences(raw))
            {
                if (sb.Length > 0 && sb.Length + sentence.Length > maxChars)
                {
                    yield return sb.ToString().Trim();
                    sb.Clear();
                }
                sb.Append(sentence);
            }
            if (sb.Length > 0) yield return sb.ToString().Trim();
        }
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?' or '؟' or '۔' or '\n' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                yield return text[start..(i + 1)] + " ";
                start = i + 1;
            }
            else if (i - start > 1500)
            {
                // Hard split for text without punctuation.
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    public static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    /// <summary>Rough token estimate (≈4 chars/token for Latin, ≈2.5 for Arabic).</summary>
    public static int EstimateTokens(string text)
    {
        var arabic = text.Count(ArabicText.IsArabicLetter);
        var other = text.Length - arabic;
        return (int)Math.Ceiling(arabic / 2.5 + other / 4.0);
    }
}

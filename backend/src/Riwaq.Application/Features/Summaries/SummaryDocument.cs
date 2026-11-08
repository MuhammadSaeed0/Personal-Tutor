using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Ingestion;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Summaries;

/// <summary>What the PDF needs to know about the summary's source.</summary>
public sealed record SummarySource(string Title, SourceKind Kind, IReadOnlyDictionary<int, int> PageStartSeconds);

/// <summary>
/// Turns a generated summary into a printable HTML document: Markdown without raw HTML (the text comes from a model
/// fed with untrusted document content), math left as <c>\( \)</c> / <c>\[ \]</c> for KaTeX, citations written out as
/// pages, photos or video times, Arabic right-to-left with each paragraph taking its own direction.
/// </summary>
public static partial class SummaryDocument
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseMathematics()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseListExtras()
        .UseAutoLinks()
        .Build();

    public static string Html(GeneratedSummary summary, SummarySource source, DateTime generatedAt)
    {
        var ar = summary.Language == "ar";
        var citations = new List<string>();
        string Cite(int from, int to)
        {
            citations.Add(Location(source, from, to, ar));
            return $"RWCITE{citations.Count - 1}X";
        }

        var text = PrepareMath(summary.Content);
        text = PageMarker().Replace(text, m => Cite(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : int.Parse(m.Groups[1].Value)));
        var markers = summary.Citations.GroupBy(c => c.Marker).ToDictionary(g => g.Key, g => g.First());
        text = NumberMarker().Replace(text, m => markers.TryGetValue(int.Parse(m.Groups[1].Value), out var c) ? Cite(c.PageStart, c.PageEnd) : m.Value);

        var body = Markdown.ToHtml(text, Pipeline);
        body = CitePlaceholder().Replace(body, m => $"<span class=\"cite\"><bdi>{Encode(citations[int.Parse(m.Groups[1].Value)])}</bdi></span>");
        body = IsolateLatin(body);
        body = EquationParagraph().Replace(body, "<p class=\"eq\">$1</p>");

        var html = new StringBuilder();
        html.Append($"<!doctype html><html lang=\"{Encode(summary.Language)}\" dir=\"{(ar ? "rtl" : "ltr")}\"><head><meta charset=\"utf-8\">");
        html.Append($"<title>{Encode(summary.Title)}</title><style>{Css}</style></head><body>");
        html.Append("<header class=\"doc-head\">");
        html.Append($"<div class=\"brand\">{(ar ? "رِواق" : "Riwaq")}</div>");
        html.Append($"<h1 class=\"title\">{IsolateLatin(Encode(summary.Title))}</h1>");
        html.Append("<p class=\"meta\">");
        html.Append($"<span>{(ar ? "المصدر:" : "Source:")} <bdi>{Encode(source.Title)}</bdi></span>");
        html.Append($"<span>{Encode(StyleName(summary.Style, ar))}</span>");
        html.Append($"<span><bdi>{generatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</bdi></span>");
        html.Append("</p></header>");

        if (summary.Warnings.Count > 0)
        {
            html.Append("<aside class=\"warnings\">");
            foreach (var warning in summary.Warnings) html.Append($"<p>{IsolateLatin(Encode(warning))}</p>");
            html.Append("</aside>");
        }

        html.Append($"<main class=\"prose\">{body}</main>");
        html.Append($"<p class=\"disclaimer\">{Encode(Disclaimer(ar))}</p>");
        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>Footer printed on each page: the disclaimer and the page counter.</summary>
    public static string Footer(GeneratedSummary summary)
    {
        var ar = summary.Language == "ar";
        return $"""
            <div style="width:100%;margin:0 14mm;font-size:8px;color:#6b6b6b;font-family:'Segoe UI',Tahoma,Arial,sans-serif;display:flex;justify-content:space-between;direction:{(ar ? "rtl" : "ltr")}">
              <span>{Encode(ar ? "رِواق · ملخص مُولَّد بالذكاء الاصطناعي" : "Riwaq · AI-generated summary")}</span>
              <span style="direction:ltr;unicode-bidi:isolate"><span class="pageNumber"></span> / <span class="totalPages"></span></span>
            </div>
            """;
    }

    public static string FileName(GeneratedSummary summary)
    {
        var safe = string.Concat(summary.Title.Split(Path.GetInvalidFileNameChars())).Trim();
        if (safe.Length == 0) safe = "summary";
        return $"{(safe.Length > 80 ? safe[..80] : safe)}.pdf";
    }

    /// <summary>"(ص 12–14)", "(صورة 3)" or "(▶ 4:10)" depending on the source kind and language.</summary>
    internal static string Location(SummarySource source, int from, int to, bool ar)
    {
        if (source.Kind == SourceKind.YouTube && source.PageStartSeconds.TryGetValue(from, out var seconds))
            return $"(▶ {TranscriptSegmenter.FormatTime(seconds)})";
        var range = to > from ? $"{from}–{to}" : $"{from}";
        var word = (source.Kind, ar) switch
        {
            (SourceKind.Images, true) => to > from ? "الصور" : "صورة",
            (SourceKind.Images, false) => to > from ? "photos" : "photo",
            (_, true) => "ص",
            _ => "p."
        };
        return $"({word} {range})";
    }

    /// <summary>
    /// Normalises math for Markdig: <c>\( \)</c> / <c>\[ \]</c> become <c>$</c> / <c>$$</c> (Markdig would eat the
    /// backslash), and bare <c>\ce{…}</c> outside math is wrapped in <c>$…$</c> like the web app does.
    /// </summary>
    internal static string PrepareMath(string text)
    {
        text = DisplayBrackets().Replace(text, m => $"$${m.Groups[1].Value}$$");
        text = InlineParens().Replace(text, m => $"${m.Groups[1].Value}$");
        var result = new StringBuilder();
        var last = 0;
        foreach (Match math in MathSpan().Matches(text))
        {
            result.Append(BareChemistry().Replace(text[last..math.Index], m => $"${m.Value}$"));
            result.Append(math.Value);
            last = math.Index + math.Length;
        }
        result.Append(BareChemistry().Replace(text[last..], m => $"${m.Value}$"));
        return result.ToString();
    }

    /// <summary>
    /// Wraps Latin runs and number ranges ("5–8") in text nodes in <c>&lt;bdi dir="ltr"&gt;</c> so they keep their
    /// order inside Arabic.
    /// </summary>
    internal static string IsolateLatin(string html)
    {
        var result = new StringBuilder();
        var depthMath = 0;
        foreach (Match part in TagOrText().Matches(html))
        {
            var token = part.Value;
            if (token.StartsWith('<'))
            {
                if (token.StartsWith("<span class=\"math", StringComparison.Ordinal) || token.StartsWith("<div class=\"math", StringComparison.Ordinal)
                    || token.StartsWith("<code", StringComparison.Ordinal) || token.StartsWith("<pre", StringComparison.Ordinal)) depthMath++;
                else if (depthMath > 0 && (token is "</span>" or "</div>" or "</code>" or "</pre>")) depthMath--;
                result.Append(token);
            }
            else
            {
                // Entities such as &lt; or &#39; are left alone; only real Latin words are isolated.
                result.Append(depthMath > 0 ? token : LatinRun().Replace(token, m => m.Value.StartsWith('&') ? m.Value : $"<bdi dir=\"ltr\">{m.Value}</bdi>"));
            }
        }
        return result.ToString();
    }

    private static string StyleName(SummaryStyle style, bool ar) => style switch
    {
        SummaryStyle.Quick => ar ? "ملخص سريع" : "Quick summary",
        SummaryStyle.Simple => ar ? "شرح مبسّط" : "Simple explanation",
        SummaryStyle.Detailed => ar ? "ملخص مفصّل" : "Detailed summary",
        SummaryStyle.KeyPoints => ar ? "أهم النقاط" : "Key points",
        SummaryStyle.Definitions => ar ? "المصطلحات والتعريفات" : "Definitions",
        SummaryStyle.Formulas => ar ? "القوانين والمعادلات" : "Formulas",
        SummaryStyle.Comparisons => ar ? "مقارنات" : "Comparisons",
        SummaryStyle.CausesEffects => ar ? "الأسباب والنتائج" : "Causes and effects",
        SummaryStyle.ExamQuestions => ar ? "أسئلة امتحانية" : "Exam questions",
        _ => ar ? "ورقة مراجعة" : "Revision sheet"
    };

    private static string Disclaimer(bool ar) => ar
        ? "هذا الملخص مُولَّد بالذكاء الاصطناعي من محتوى المصدر، وقد يحتوي على أخطاء. راجع المعلومات المهمة في كتابك ومع معلّمك."
        : "This summary was generated by AI from the source content and may contain mistakes. Check important details against your book and teacher.";

    private static string Encode(string text) => WebUtility.HtmlEncode(text).Replace("&#39;", "'");

    private const string Css = """
        @page { size: A4; }
        * { box-sizing: border-box; }
        html { font-size: 11.5pt; }
        body { margin: 0; color: #1d1d1b; font-family: 'Segoe UI', 'Noto Naskh Arabic', 'Noto Sans Arabic', Tahoma, Arial, sans-serif; line-height: 1.85; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
        .doc-head { border-bottom: 2px solid #0f6b5c; padding-bottom: 10px; margin-bottom: 18px; }
        .brand { color: #0f6b5c; font-weight: 700; font-size: 0.85rem; letter-spacing: 0.02em; }
        .title { font-size: 1.7rem; line-height: 1.4; margin: 4px 0 6px; }
        .meta { margin: 0; color: #5d5d58; font-size: 0.85rem; display: flex; flex-wrap: wrap; gap: 6px 16px; }
        .warnings { background: #fff6e0; border-inline-start: 4px solid #d99a1f; padding: 8px 12px; margin-bottom: 16px; font-size: 0.9rem; }
        .warnings p { margin: 2px 0; }
        p, li, td, th, h1, h2, h3, h4, blockquote { unicode-bidi: plaintext; text-align: start; }
        h2 { font-size: 1.3rem; color: #0f4f45; margin: 1.3em 0 0.4em; break-after: avoid; }
        h3 { font-size: 1.12rem; margin: 1.1em 0 0.3em; break-after: avoid; }
        h4 { font-size: 1rem; margin: 1em 0 0.3em; break-after: avoid; }
        p { margin: 0.45em 0; }
        ul, ol { padding-inline-start: 1.5em; margin: 0.4em 0; }
        li { margin: 0.2em 0; break-inside: avoid; }
        strong { color: #111; }
        table { border-collapse: collapse; width: 100%; margin: 0.8em 0; font-size: 0.92rem; break-inside: auto; }
        tr { break-inside: avoid; }
        th, td { border: 1px solid #cfcbbf; padding: 5px 8px; vertical-align: top; }
        th { background: #eef4f1; }
        blockquote { margin: 0.6em 0; padding: 4px 12px; border-inline-start: 3px solid #0f6b5c; background: #f6f8f6; }
        code, pre { direction: ltr; unicode-bidi: isolate; font-family: Consolas, 'Courier New', monospace; font-size: 0.9em; }
        pre { text-align: left; background: #f4f4f1; padding: 8px 10px; border-radius: 4px; white-space: pre-wrap; }
        .katex { direction: ltr; unicode-bidi: isolate; font-size: 1.08em; }
        .katex-display { margin: 0.6em 0; overflow: hidden; }
        .katex-display, .katex-display > .katex, p.eq { text-align: center !important; }
        p.eq { unicode-bidi: normal; margin: 0.7em 0; }
        .cite { color: #0f6b5c; font-size: 0.82em; white-space: nowrap; }
        .disclaimer { margin-top: 24px; padding-top: 8px; border-top: 1px solid #ddd; color: #6b6b6b; font-size: 0.82rem; }
        """;

    [GeneratedRegex(@"\[p:(\d+)(?:-(\d+))?\]")]
    private static partial Regex PageMarker();

    [GeneratedRegex(@"\[(\d{1,2})\](?!\()")]
    private static partial Regex NumberMarker();

    [GeneratedRegex(@"<p>\s*(<span class=""math[^""]*"">[^<]*</span>)\s*</p>")]
    private static partial Regex EquationParagraph();

    [GeneratedRegex(@"RWCITE(\d+)X")]
    private static partial Regex CitePlaceholder();

    [GeneratedRegex(@"\\\[([\s\S]+?)\\\]")]
    private static partial Regex DisplayBrackets();

    [GeneratedRegex(@"\\\(([\s\S]+?)\\\)")]
    private static partial Regex InlineParens();

    [GeneratedRegex(@"\$\$[\s\S]+?\$\$|\$[^\n$]+?\$")]
    private static partial Regex MathSpan();

    [GeneratedRegex(@"\\ce\{(?:[^{}]|\{[^{}]*\})*\}")]
    private static partial Regex BareChemistry();

    [GeneratedRegex(@"<[^>]+>|[^<]+")]
    private static partial Regex TagOrText();

    [GeneratedRegex(@"[A-Za-z](?:[A-Za-z0-9.,'’+\-/= ]|&amp;)*[A-Za-z0-9]|[A-Za-z]|\d+(?:[–-]\d+)+|&#?[A-Za-z0-9]+;")]
    private static partial Regex LatinRun();
}

/// <summary>Builds a summary's PDF for its owner.</summary>
public sealed class SummaryPdfService(IAppDbContext db, IPdfRenderer renderer, TimeProvider clock)
{
    public async Task<(string FileName, byte[] Pdf)> RenderAsync(Guid userId, Guid summaryId, CancellationToken ct)
    {
        var summary = await db.Summaries.AsNoTracking().FirstOrDefaultAsync(s => s.Id == summaryId && s.UserId == userId, ct)
                      ?? throw new NotFoundException("Summary");
        if (!renderer.IsAvailable)
            throw new ServiceUnavailableException("pdf_unavailable", "PDF export needs Microsoft Edge or Google Chrome on the server.");

        var book = await db.Books.AsNoTracking().Where(b => b.Id == summary.BookId).Select(b => new { b.Title, b.Kind }).FirstOrDefaultAsync(ct);
        var times = book?.Kind == SourceKind.YouTube
            ? await db.DocumentPages.AsNoTracking().Where(p => p.BookId == summary.BookId && p.StartSeconds != null)
                .ToDictionaryAsync(p => p.PageNumber, p => p.StartSeconds!.Value, ct)
            : new Dictionary<int, int>();
        var source = new SummarySource(book?.Title ?? string.Empty, book?.Kind ?? SourceKind.Pdf, times);

        var html = SummaryDocument.Html(summary, source, summary.UpdatedAt == default ? clock.GetUtcNow().UtcDateTime : summary.UpdatedAt);
        var pdf = await renderer.RenderAsync(html, SummaryDocument.Footer(summary), ct);
        return (SummaryDocument.FileName(summary), pdf);
    }
}

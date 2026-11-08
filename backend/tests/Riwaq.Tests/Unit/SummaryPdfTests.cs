using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Riwaq.Application.Features.Summaries;
using Riwaq.Domain;
using Riwaq.Domain.Entities;
using Riwaq.Infrastructure.Pdf;

namespace Riwaq.Tests.Unit;

public class SummaryDocumentTests
{
    private static readonly SummarySource Pdf = new("كتاب الفيزياء", SourceKind.Pdf, new Dictionary<int, int>());

    private static GeneratedSummary Summary(string content, string language = "ar", params SourceReference[] citations) => new()
    {
        Title = "ملخص قانون نيوتن",
        Content = content,
        Language = language,
        Style = SummaryStyle.KeyPoints,
        Citations = [.. citations]
    };

    [Fact]
    public void Raw_html_from_the_model_is_escaped()
    {
        var html = SummaryDocument.Html(Summary("نص <script>alert(1)</script> و <img src=x onerror=alert(1)>"), Pdf, DateTime.UtcNow);

        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;<bdi dir=\"ltr\">script</bdi>&gt;", html);
        Assert.DoesNotContain("&<bdi", html);
    }

    [Fact]
    public void Page_references_and_numbered_citations_become_readable_locations()
    {
        var html = SummaryDocument.Html(
            Summary("القوة تساوي الكتلة في العجلة [p:12]. وحدة القوة النيوتن [p:3-4]. انظر أيضًا [1].", "ar", new SourceReference { Marker = 1, PageStart = 7, PageEnd = 7 }),
            Pdf, DateTime.UtcNow);

        Assert.Contains("(ص 12)", html);
        Assert.Contains("(ص <bdi dir=\"ltr\">3–4</bdi>)", html); // the range keeps its order inside Arabic
        Assert.Contains("(ص 7)", html);
        Assert.DoesNotContain("[p:", html);
        Assert.DoesNotContain("[1]", html);
    }

    [Fact]
    public void Video_and_photo_sources_cite_times_and_photos()
    {
        var video = new SummarySource("درس المعادن", SourceKind.YouTube, new Dictionary<int, int> { [6] = 590 });
        var photos = new SummarySource("كراسة", SourceKind.Images, new Dictionary<int, int>());

        Assert.Equal("(▶ 9:50)", SummaryDocument.Location(video, 6, 7, true));
        Assert.Equal("(صورة 2)", SummaryDocument.Location(photos, 2, 2, true));
        Assert.Equal("(photos 2–3)", SummaryDocument.Location(photos, 2, 3, false));
        Assert.Equal("(p. 5)", SummaryDocument.Location(Pdf, 5, 5, false));
    }

    [Fact]
    public void Math_is_kept_for_katex_and_bare_chemistry_is_wrapped()
    {
        Assert.Equal("$F = ma$ و $$E=mc^2$$ و $\\ce{H2O}$",
            SummaryDocument.PrepareMath("\\(F = ma\\) و \\[E=mc^2\\] و \\ce{H2O}"));
        Assert.Equal("$x \\ce{CO2}$", SummaryDocument.PrepareMath("$x \\ce{CO2}$"));

        var html = SummaryDocument.Html(Summary("القانون $F = m a$ ومعادلة \\ce{2H2 + O2 -> 2H2O}"), Pdf, DateTime.UtcNow);
        Assert.Contains("class=\"math\"", html);
        Assert.Contains("\\(F = m a\\)", html);
        Assert.Contains("\\ce{2H2 + O2 -&gt; 2H2O}", html);
    }

    [Fact]
    public void English_runs_inside_arabic_are_isolated_but_math_is_not()
    {
        var html = SummaryDocument.IsolateLatin("<p>تسمى القوة Force وتقاس بالنيوتن</p><span class=\"math inline\">\\(F = ma\\)</span>");

        Assert.Contains("<bdi dir=\"ltr\">Force</bdi>", html);
        Assert.Contains("<span class=\"math inline\">\\(F = ma\\)</span>", html);
        Assert.Contains("ص <bdi dir=\"ltr\">5–8</bdi>", SummaryDocument.IsolateLatin("<h1>ملخص — ص 5–8</h1>"));
    }

    [Fact]
    public void Document_direction_follows_the_summary_language()
    {
        Assert.Contains("dir=\"rtl\"", SummaryDocument.Html(Summary("نص"), Pdf, DateTime.UtcNow));
        Assert.Contains("dir=\"ltr\"", SummaryDocument.Html(Summary("Text", "en"), Pdf, DateTime.UtcNow));
        Assert.Equal("ملخص قانون نيوتن.pdf", SummaryDocument.FileName(Summary("x")));
    }
}

/// <summary>Runs only when Edge or Chrome is installed: prints a real summary to PDF.</summary>
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (BrowserPdfRenderer.FindBrowser(null) is null) Skip = "No Edge or Chrome found for PDF rendering.";
    }
}

public class BrowserPdfRendererTests
{
    [BrowserFact]
    public async Task Prints_an_arabic_summary_with_math_to_pdf()
    {
        await using var renderer = new BrowserPdfRenderer(Options.Create(new PdfRenderOptions()), NullLogger<BrowserPdfRenderer>.Instance);
        var summary = new GeneratedSummary
        {
            Title = "ملخص الحركة", Language = "ar", Style = SummaryStyle.Detailed,
            Content = "## قانون نيوتن الثاني\n\nالقوة المحصلة $F = m a$ [p:3].\n\n| الكمية | الوحدة |\n|---|---|\n| القوة | N |\n\n\\ce{2H2 + O2 -> 2H2O}"
        };
        var source = new SummarySource("كتاب الفيزياء", SourceKind.Pdf, new Dictionary<int, int>());

        var pdf = await renderer.RenderAsync(SummaryDocument.Html(summary, source, DateTime.UtcNow), SummaryDocument.Footer(summary), CancellationToken.None);

        Assert.True(pdf.Length > 5000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}

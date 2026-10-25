using System.Text.Json;
using Riwaq.Application.Abstractions;
using Riwaq.Tests.Support;

namespace Riwaq.Tests.Integration;

/// <summary>Scanned (image-only) pages read by the vision model: transcription, reuse on reprocess, usage limits.</summary>
public sealed class ScannedBookTests : IAsyncLifetime
{
    private readonly RiwaqApiFactory _factory = new() { OcrEnabled = true };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _factory.DisposeAsync();

    private static string Transcription(string text, string figure) => JsonSerializer.Serialize(new
    {
        text,
        figures = new[] { new { caption = "شكل (1)", description = figure } },
        legibility = "high"
    });

    [Fact]
    public async Task Scanned_pages_are_transcribed_by_the_vision_model_and_reused_on_reprocess()
    {
        _factory.Chat.Respond("ocr", request =>
        {
            var image = Assert.Single(request.Images!);
            Assert.Equal("image/jpeg", image.MediaType);
            Assert.True(image.Data.Length > 1000);
            Assert.NotNull(request.JsonSchema);
            return Transcription("المعدن مادة صلبة متجانسة تتكون طبيعيًا ولها تركيب كيميائي محدد.", "بلورات مكعبة الشكل لمعدن الهاليت");
        });
        var client = await _factory.CreateStudentAsync();

        var bookId = (await (await client.UploadPdfAsync(TestPdf.ImageOnly(), title: "Scanned")).ReadJsonAsync()).GetProperty("id").GetGuid();
        var status = await client.WaitForProcessingAsync(bookId);
        Assert.Contains(status.GetProperty("bookStatus").GetString(), new[] { "ready", "readyWithWarnings" });

        var page = await (await client.GetAsync($"/api/books/{bookId}/pages/1/text")).ReadJsonAsync();
        Assert.Equal("ocr", page.GetProperty("extractionMethod").GetString());
        var text = page.GetProperty("text").GetString()!;
        Assert.Contains("المعدن مادة صلبة", text);
        Assert.Contains("[شكل: شكل (1) — بلورات مكعبة الشكل لمعدن الهاليت]", text);

        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        var codes = detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        Assert.DoesNotContain("scanned_page_no_ocr", codes);
        Assert.DoesNotContain("no_text_extracted", codes);

        // Reprocessing keeps the transcription without asking the model again.
        var ocrCalls = _factory.Chat.Requests.Count(r => r.Feature == "ocr");
        (await client.PostAsync($"/api/books/{bookId}/reprocess", null)).EnsureSuccessStatusCode();
        await client.WaitForProcessingAsync(bookId);
        Assert.Equal(ocrCalls, _factory.Chat.Requests.Count(r => r.Feature == "ocr"));
        Assert.Contains("المعدن مادة صلبة", (await (await client.GetAsync($"/api/books/{bookId}/pages/1/text")).ReadJsonAsync()).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Scans_with_another_tools_invisible_text_layer_are_read_again()
    {
        _factory.Chat.Respond("ocr", _ => Transcription("الصخور النارية تتكون من تبلور الصهارة.", "قطاع في بركان"));
        var client = await _factory.CreateStudentAsync();

        var bookId = (await (await client.UploadPdfAsync(TestPdf.ScanWithInvisibleOcrLayer(), title: "Pre-OCR'd scan")).ReadJsonAsync()).GetProperty("id").GetGuid();
        await client.WaitForProcessingAsync(bookId);

        var page = await (await client.GetAsync($"/api/books/{bookId}/pages/1/text")).ReadJsonAsync();
        Assert.Equal("ocr", page.GetProperty("extractionMethod").GetString());
        Assert.True(page.GetProperty("isScanned").GetBoolean());
        Assert.Contains("الصخور النارية", page.GetProperty("text").GetString());
        Assert.DoesNotContain("dla_c]", page.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Reaching_the_usage_limit_is_reported_per_page()
    {
        _factory.Chat.Respond("ocr", _ => throw new AiUnavailableException("ai_rate_limited", "limit"));
        var client = await _factory.CreateStudentAsync();

        var bookId = (await (await client.UploadPdfAsync(TestPdf.ImageOnly(), title: "Scanned")).ReadJsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal("readyWithWarnings", (await client.WaitForProcessingAsync(bookId)).GetProperty("bookStatus").GetString());

        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        var codes = detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        Assert.Contains("ocr_rate_limited", codes);
    }
}

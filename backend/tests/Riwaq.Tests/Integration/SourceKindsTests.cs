using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Riwaq.Tests.Support;
using SkiaSharp;

namespace Riwaq.Tests.Integration;

/// <summary>YouTube and photo sources end to end: creation, processing, chapters, retrieval by time, photo pages.</summary>
public sealed partial class SourceKindsTests : IAsyncLifetime
{
    private const int Geology = 7;
    private readonly RiwaqApiFactory _factory = new() { OcrEnabled = true };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_youtube_video_becomes_timed_pages_with_topic_sections_and_time_citations()
    {
        _factory.Chat.Respond("transcript", request =>
        {
            var segments = SegmentRegex().Matches(request.Messages[^1].Content)
                .Select(m => new { index = int.Parse(m.Groups[1].Value), text = "تنقيح: " + m.Groups[2].Value.Trim() + "." })
                .ToList();
            var topics = segments.Any(s => s.index == 1)
                ? new object[] { new { startIndex = 1, title = "المعادن" }, new { startIndex = 3, title = "الصخور النارية" } }
                : [];
            return JsonSerializer.Serialize(new { windows = segments, topics });
        });
        _factory.Chat.Respond("tutor", _ => "الصخور النارية تتكون من تبلور الصهارة [1].");
        var client = await _factory.CreateStudentAsync();

        var created = await client.PostAsync("/api/books/youtube", ApiTestHelpers.JsonBody(new { url = $"https://youtu.be/{FakeVideoSource.LessonId}?t=30", subjectId = Geology }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var book = await created.ReadJsonAsync();
        Assert.Equal("youTube", book.GetProperty("kind").GetString());
        Assert.Equal("درس المعادن والصخور", book.GetProperty("title").GetString());
        Assert.Equal(400, book.GetProperty("durationSeconds").GetInt32());
        var bookId = book.GetProperty("id").GetGuid();

        Assert.Equal("ready", (await client.WaitForProcessingAsync(bookId)).GetProperty("bookStatus").GetString());
        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        var pages = detail.GetProperty("pages").EnumerateArray().ToList();
        Assert.Equal(4, pages.Count);
        Assert.Equal((0, 120), (pages[0].GetProperty("startSeconds").GetInt32(), pages[0].GetProperty("endSeconds").GetInt32()));
        Assert.All(pages, p => Assert.Equal("transcript", p.GetProperty("extractionMethod").GetString()));

        var chapters = detail.GetProperty("chapters").EnumerateArray()
            .Select(c => (c.GetProperty("title").GetString(), c.GetProperty("startPage").GetInt32(), c.GetProperty("endPage").GetInt32(), c.GetProperty("source").GetString()))
            .ToList();
        Assert.Equal([("المعادن", 1, 2, "topics"), ("الصخور النارية", 3, 4, "topics")], chapters);
        var codes = detail.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        Assert.Contains("transcript_auto_generated", codes);
        Assert.Contains("topics_generated", codes);

        var page = await (await client.GetAsync($"/api/books/{bookId}/pages/3/text")).ReadJsonAsync();
        Assert.StartsWith("تنقيح: الصخور النارية", page.GetProperty("text").GetString());

        // The tutor sees transcript passages labelled with their time, not a page number.
        var conversation = await (await client.PostAsync("/api/tutor/conversations", ApiTestHelpers.JsonBody(new { bookId }))).ReadJsonAsync();
        var ask = await client.PostAsync($"/api/tutor/conversations/{conversation.GetProperty("id").GetGuid()}/messages",
            ApiTestHelpers.JsonBody(new { question = "كيف تتكون الصخور النارية؟", language = "ar" }));
        await SseReader.ReadAllAsync(ask);
        var tutorRequest = _factory.Chat.Requests.Last(r => r.Feature == "tutor");
        Assert.Matches("<passage id=\"1\" time=\"\\d+:\\d\\d-\\d+:\\d\\d\"", tutorRequest.Messages[^1].Content);
        Assert.Contains("video", tutorRequest.SystemPrompt);

        // A video has no downloadable file, and the same video cannot be added twice.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/books/{bookId}/file")).StatusCode);
        var again = await client.PostAsync("/api/books/youtube", ApiTestHelpers.JsonBody(new { url = $"https://www.youtube.com/watch?v={FakeVideoSource.LessonId}", subjectId = Geology }));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("https://example.com/watch?v=AAAAAAAAAAA", "video_url_invalid")]
    [InlineData("https://youtu.be/" + FakeVideoSource.SilentId, "video_no_captions")]
    [InlineData("https://youtu.be/CCCCCCCCCCC", "video_unavailable")]
    public async Task Unusable_videos_are_rejected_with_a_clear_code(string url, string code)
    {
        var client = await _factory.CreateStudentAsync();
        var response = await client.PostAsync("/api/books/youtube", ApiTestHelpers.JsonBody(new { url, subjectId = Geology }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ErrorCode(response));
    }

    [Fact]
    public async Task Photos_become_pages_read_by_the_vision_model_and_more_can_be_added()
    {
        var reads = 0;
        _factory.Chat.Respond("ocr", request =>
        {
            Assert.Contains("photographed", request.Messages[0].Content);
            Interlocked.Increment(ref reads);
            using var bitmap = SKBitmap.Decode(request.Images![0].Data);
            var n = bitmap.Width - 800;
            var heading = n switch { 1 => "# الفصل الأول: المعادن", 2 => "# الفصل الثاني: الصخور", _ => "تابع الصخور الرسوبية" };
            return JsonSerializer.Serialize(new { text = $"{heading}\n\nنص الصورة رقم {n}.", figures = Array.Empty<object>(), legibility = "high" });
        });
        var client = await _factory.CreateStudentAsync();

        using var form = new MultipartFormDataContent();
        form.Add(Image("page-1.png", SKEncodedImageFormat.Png), "files", "page-1.png");
        form.Add(Image("page-2.jpg", SKEncodedImageFormat.Jpeg), "files", "page-2.jpg");
        form.Add(new StringContent(Geology.ToString()), "subjectId");
        form.Add(new StringContent("كراسة الجيولوجيا"), "title");
        var created = await client.PostAsync("/api/books/images", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var book = await created.ReadJsonAsync();
        Assert.Equal("images", book.GetProperty("kind").GetString());
        Assert.Equal(2, book.GetProperty("pageCount").GetInt32());
        var bookId = book.GetProperty("id").GetGuid();

        await client.WaitForProcessingAsync(bookId);
        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        Assert.Equal(["الفصل الأول: المعادن", "الفصل الثاني: الصخور"], detail.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("title").GetString()));
        var photo = await client.GetAsync($"/api/books/{bookId}/images/2");
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/books/{bookId}/images/3")).StatusCode);

        // Adding a photo reads only the new page.
        using var more = new MultipartFormDataContent();
        more.Add(Image("page-3.png", SKEncodedImageFormat.Png), "files", "page-3.png");
        (await client.PostAsync($"/api/books/{bookId}/images", more)).EnsureSuccessStatusCode();
        await client.WaitForProcessingAsync(bookId);
        Assert.Equal(3, reads);
        var page3 = await (await client.GetAsync($"/api/books/{bookId}/pages/3/text")).ReadJsonAsync();
        Assert.Contains("نص الصورة رقم 3", page3.GetProperty("text").GetString());
        Assert.Contains("نص الصورة رقم 1", (await (await client.GetAsync($"/api/books/{bookId}/pages/1/text")).ReadJsonAsync()).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Files_that_are_not_images_are_rejected()
    {
        var client = await _factory.CreateStudentAsync();
        using var form = new MultipartFormDataContent();
        var fake = new ByteArrayContent("not an image at all"u8.ToArray());
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fake, "files", "notes.png");
        form.Add(new StringContent(Geology.ToString()), "subjectId");

        var response = await client.PostAsync("/api/books/images", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("image_unreadable", await ErrorCode(response));
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    /// <summary>A page photo whose width encodes its page number (800 + page), so the fake model knows which page it reads.</summary>
    private static ByteArrayContent Image(string name, SKEncodedImageFormat format)
    {
        var page = int.Parse(name.Split('-', '.')[1]);
        using var surface = SKSurface.Create(new SKImageInfo(800 + page, 1100));
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.DarkSlateGray };
        surface.Canvas.DrawRect(80, 120, 600, 60, paint);
        surface.Canvas.DrawRect(80, 260, 520 + name.Length, 30, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        var content = new ByteArrayContent(data.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(format == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg");
        return content;
    }

    [GeneratedRegex("<segment index=\"(\\d+)\"[^>]*>\\n([^<]*)</segment>")]
    private static partial Regex SegmentRegex();
}

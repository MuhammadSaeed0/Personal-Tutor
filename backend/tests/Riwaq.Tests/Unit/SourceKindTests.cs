using Riwaq.Application.Abstractions;
using Riwaq.Application.Ingestion;
using Riwaq.Domain;
using Riwaq.Infrastructure.Media;
using SkiaSharp;

namespace Riwaq.Tests.Unit;

public class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=Jc1nugDYX2c", "Jc1nugDYX2c")]
    [InlineData("https://youtube.com/watch?feature=share&v=Jc1nugDYX2c&t=42s", "Jc1nugDYX2c")]
    [InlineData("https://m.youtube.com/watch?v=Jc1nugDYX2c", "Jc1nugDYX2c")]
    [InlineData("https://youtu.be/Jc1nugDYX2c?si=abc", "Jc1nugDYX2c")]
    [InlineData("youtu.be/Jc1nugDYX2c", "Jc1nugDYX2c")]
    [InlineData("https://www.youtube.com/shorts/Jc1nugDYX2c", "Jc1nugDYX2c")]
    [InlineData("https://www.youtube.com/embed/Jc1nugDYX2c", "Jc1nugDYX2c")]
    [InlineData("https://www.youtube.com/live/Jc1nugDYX2c?feature=shared", "Jc1nugDYX2c")]
    [InlineData(" Jc1nugDYX2c ", "Jc1nugDYX2c")]
    public void Recognises_youtube_link_forms(string input, string id) => Assert.Equal(id, YouTubeUrl.TryGetVideoId(input));

    [Theory]
    [InlineData("")]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/@channel")]
    [InlineData("https://evil.example/youtu.be/Jc1nugDYX2c")]
    [InlineData("javascript:alert(1)")]
    public void Rejects_other_links(string input) => Assert.Null(YouTubeUrl.TryGetVideoId(input));
}

public class TranscriptSegmenterTests
{
    [Fact]
    public void Groups_captions_into_two_minute_segments_and_drops_noise()
    {
        var captions = new List<VideoCaption>
        {
            new(0, 4, "[موسيقى]"),
            new(4, 5, "السلام عليكم"),
            new(9, 5, "السلام عليكم"), // auto-captions repeat lines
            new(14, 60, "النهاردة هنتكلم عن &amp; المعادن"),
            new(74, 50, "<c>تعريف</c> المعدن"),
            new(124, 30, "خواص المعادن [Music]"),
            new(154, 10, "الصلادة")
        };

        var windows = TranscriptSegmenter.Segment(captions);

        Assert.Equal(2, windows.Count);
        Assert.Equal(new TranscriptWindow(1, 4, 124, "السلام عليكم النهاردة هنتكلم عن & المعادن تعريف المعدن"), windows[0]);
        Assert.Equal(new TranscriptWindow(2, 124, 164, "خواص المعادن الصلادة"), windows[1]);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(75, "1:15")]
    [InlineData(3725, "1:02:05")]
    public void Formats_times(int seconds, string text) => Assert.Equal(text, TranscriptSegmenter.FormatTime(seconds));
}

public class TranscriptStructurerTests
{
    private static readonly TranscriptWindow[] Batch =
    [
        new(1, 0, 120, "المعدن مادة صلبة متجانسة تتكون طبيعيا ولها تركيب كيميائي محدد"),
        new(2, 120, 240, "الصخور النارية تتكون من تبلور الصهارة في باطن الارض او على سطحها")
    ];

    [Fact]
    public void Keeps_cleaned_text_and_valid_topics()
    {
        var (texts, topics) = TranscriptStructurer.Parse(
            """{"windows":[{"index":1,"text":"المعدن مادة صلبة متجانسة، تتكون طبيعيًا ولها تركيب كيميائي محدد."},{"index":2,"text":"الصخور النارية تتكون من تبلور الصهارة في باطن الأرض أو على سطحها."}],"topics":[{"startIndex":1,"title":"المعادن"},{"startIndex":9,"title":"خارج الدفعة"}]}""",
            Batch);

        Assert.StartsWith("المعدن مادة صلبة متجانسة،", texts[1]);
        Assert.Equal([new TranscriptTopic(1, "المعادن")], topics);
    }

    [Fact]
    public void Falls_back_to_raw_captions_when_text_was_dropped_or_is_missing()
    {
        var (texts, _) = TranscriptStructurer.Parse("""{"windows":[{"index":1,"text":"المعدن."}],"topics":[]}""", Batch);
        Assert.Equal(Batch[0].Text, texts[1]);
        Assert.Equal(Batch[1].Text, texts[2]);

        var (fromInvalid, topics) = TranscriptStructurer.Parse("not json", Batch);
        Assert.Equal(2, fromInvalid.Count);
        Assert.Empty(topics);
    }

    [Fact]
    public void Topics_become_sections_spanning_until_the_next_topic()
    {
        var sections = StructureDetector.FromTopics([("الصخور", 4), ("المعادن", 1), ("خارج النطاق", 40)], 10);

        Assert.Equal([("المعادن", 1, 3), ("الصخور", 4, 10)], sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
        Assert.All(sections, s => Assert.Equal(ChapterSource.Topics, s.Source));
    }
}

public class SkiaImageProcessorTests
{
    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.SkyBlue);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public void Converts_png_to_jpeg_and_scales_large_photos_down()
    {
        var result = new SkiaImageProcessor().Normalize(Encode(4000, 3000, SKEncodedImageFormat.Png));

        Assert.NotNull(result);
        Assert.Equal((SkiaImageProcessor.MaxSide, 1800), (result!.Width, result.Height));
        Assert.Equal([0xFF, 0xD8], result.Jpeg[..2]); // JPEG signature
    }

    [Fact]
    public void Keeps_small_images_at_their_size()
    {
        var result = new SkiaImageProcessor().Normalize(Encode(640, 480, SKEncodedImageFormat.Jpeg));
        Assert.Equal((640, 480), (result!.Width, result.Height));
    }

    [Fact]
    public void Rejects_data_that_is_not_an_image() =>
        Assert.Null(new SkiaImageProcessor().Normalize("%PDF-1.7 not an image"u8.ToArray()));
}

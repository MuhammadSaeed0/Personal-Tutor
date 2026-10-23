using Riwaq.Application.Abstractions;
using Riwaq.Application.Ingestion;
using Riwaq.Application.Text;
using Riwaq.Domain;
using Riwaq.Infrastructure.Ai;
using Riwaq.Infrastructure.Pdf;

namespace Riwaq.Tests.Unit;

public class TextProcessingTests
{
    [Fact]
    public void Folding_removes_diacritics_and_unifies_letters() =>
        Assert.Equal(ArabicText.FoldForMatching("الْمَدْرَسَة"), ArabicText.FoldForMatching("المدرسه"));

    [Fact]
    public void Cleaner_converts_presentation_forms_but_keeps_scientific_notation()
    {
        var cleaned = TextCleaner.CleanExtracted("ﻟﻠﺎ   H₂O  and m²");
        Assert.Contains("H₂O", cleaned);
        Assert.Contains("m²", cleaned);
        Assert.DoesNotContain("ﻟ", cleaned);
        Assert.DoesNotContain("   ", cleaned);
    }

    [Theory]
    [InlineData("Explain page 42", new[] { 42 })]
    [InlineData("اشرح صفحة ٤٢ من فضلك", new[] { 42 })]
    [InlineData("ما المقصود في الصفحات 10-12؟", new[] { 10, 11, 12 })]
    [InlineData("ص 7", new[] { 7 })]
    [InlineData("What is photosynthesis?", new int[0])]
    public void Page_references_are_parsed(string question, int[] expected) =>
        Assert.Equal(expected, PageReferenceParser.Parse(question));

    [Fact]
    public void Chunker_respects_chapters_and_tracks_pages()
    {
        var chapterA = new ChapterRange(Guid.NewGuid(), "A", 1, 2);
        var chapterB = new ChapterRange(Guid.NewGuid(), "B", 3, 3);
        var para = string.Join(' ', Enumerable.Repeat("Force equals mass times acceleration.", 10));
        var pages = new[]
        {
            new PageText(1, para + "\n\n" + para, ExtractionMethod.EmbeddedText),
            new PageText(2, para, ExtractionMethod.EmbeddedText),
            new PageText(3, para, ExtractionMethod.EmbeddedText)
        };

        var chunks = TextChunker.Chunk(pages, [chapterA, chapterB], targetChars: 700);

        Assert.All(chunks, c => Assert.True(c.PageStart <= c.PageEnd));
        Assert.DoesNotContain(chunks, c => c.ChapterId == chapterA.Id && c.PageEnd > 2);
        Assert.Contains(chunks, c => c.ChapterId == chapterB.Id && c.PageStart == 3);
        Assert.Equal(chunks.Select((_, i) => i), chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void Tokenizer_strips_arabic_article_and_stopwords()
    {
        var tokens = SearchTokenizer.Tokenize("ما هو التمثيل الضوئي في النبات؟");
        Assert.Contains("تمثيل", tokens);
        Assert.Contains("ضويي", tokens); // ئ folds to ي
        Assert.DoesNotContain("في", tokens);
    }

    [Fact]
    public void Language_detection()
    {
        Assert.Equal("ar", LanguageDetector.Detect(string.Concat(Enumerable.Repeat("هذا نص عربي عن القوة والحركة في الفيزياء ", 5))));
        Assert.Equal("en", LanguageDetector.Detect(string.Concat(Enumerable.Repeat("The force is equal to the mass times the acceleration of the body. ", 3))));
        Assert.Equal("fr", LanguageDetector.Detect(string.Concat(Enumerable.Repeat("La force est égale à la masse et une accélération dans le corps des objets. ", 3))));
    }

    [Fact]
    public void Local_embeddings_rank_related_text_higher()
    {
        var query = LocalHashingEmbeddingModel.Embed("Newton second law force acceleration");
        var related = LocalHashingEmbeddingModel.Embed("According to Newton's second law, force equals mass times acceleration.");
        var unrelated = LocalHashingEmbeddingModel.Embed("Photosynthesis converts light energy into chemical energy in plants.");
        Assert.True(VectorCodec.Cosine(query, related) > VectorCodec.Cosine(query, unrelated));
        Assert.Equal(query, VectorCodec.FromBytes(VectorCodec.ToBytes(query)));
    }

    [Fact]
    public void Tesseract_tsv_is_rebuilt_into_lines_and_paragraphs()
    {
        const string tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
                           "5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t95\tHello\n" +
                           "5\t1\t1\t1\t1\t2\t0\t0\t10\t10\t85\tworld\n" +
                           "5\t1\t1\t1\t2\t1\t0\t0\t10\t10\t90\tsecond\n" +
                           "5\t1\t1\t2\t1\t1\t0\t0\t10\t10\t80\tnext\n";
        var (text, conf) = TesseractOcrEngine.ParseTsv(tsv);
        Assert.Equal("Hello world\nsecond\n\nnext", text);
        Assert.Equal(87.5, conf);
    }
}

public class StructureDetectorTests
{
    [Fact]
    public void Chapters_are_found_in_recognised_text_and_the_contents_page_is_ignored()
    {
        var pages = new Dictionary<int, string>
        {
            [2] = "محتويات الكتاب\nالفصل الأول: المعادن — 5\nالفصل الثاني: الصخور النارية — 18\nالفصل الثالث: الصخور الرسوبية — 31",
            [5] = "# الفصل الأول: المعادن\n\nالمعدن مادة صلبة متجانسة.",
            [9] = "تابع الخواص الفيزيائية للمعادن.",
            [18] = "# الفصل الثاني: الصخور النارية\n\n[شكل: قطاع في بركان]"
        };

        var sections = StructureDetector.FromRecognisedText(pages, 30);

        Assert.Equal(2, sections.Count);
        Assert.Equal(("الفصل الأول: المعادن", 5, 17), (sections[0].Title, sections[0].StartPage, sections[0].EndPage));
        Assert.Equal(("الفصل الثاني: الصخور النارية", 18, 30), (sections[1].Title, sections[1].StartPage, sections[1].EndPage));
        Assert.All(sections, s => Assert.Equal(ChapterSource.HeadingHeuristic, s.Source));
    }

    [Fact]
    public void Markdown_headings_are_used_when_no_chapter_keywords_exist()
    {
        var pages = new Dictionary<int, string>
        {
            [1] = "# Minerals\nA mineral is a naturally occurring solid.",
            [4] = "Hardness is measured on the Mohs scale.",
            [6] = "# Igneous rocks\nThey form when magma cools.",
            [9] = "## Intrusive rocks\nThey cool slowly underground."
        };

        var sections = StructureDetector.FromRecognisedText(pages, 12);

        Assert.Equal(["Minerals", "Igneous rocks", "Intrusive rocks"], sections.Select(s => s.Title));
        Assert.Equal(2, sections[2].Level);
    }

    private static ExtractedPage Page(int n, params (string Text, double Size)[] lines) =>
        new(n, string.Join('\n', lines.Select(l => l.Text)), lines.Select((l, i) => new ExtractedLine(l.Text, l.Size, 800 - i * 20, false)).ToList(), 500, 0, 595, 842);

    [Fact]
    public void Outline_is_preferred_and_ranges_are_computed()
    {
        var doc = new ExtractedDocument(10, [], [
            new OutlineEntry("Chapter 1", 1, 1), new OutlineEntry("Lesson 1.1", 2, 2), new OutlineEntry("Chapter 2", 6, 1)
        ]);
        var sections = StructureDetector.Detect(doc);
        Assert.Equal(3, sections.Count);
        Assert.Equal((1, 5), (sections[0].StartPage, sections[0].EndPage));
        Assert.Equal((2, 5), (sections[1].StartPage, sections[1].EndPage));
        Assert.Equal((6, 10), (sections[2].StartPage, sections[2].EndPage));
        Assert.All(sections, s => Assert.Equal(ChapterSource.PdfOutline, s.Source));
    }

    [Fact]
    public void Keyword_headings_are_detected_and_running_headers_ignored()
    {
        var body = ("Body text about forces and motion in everyday life.", 11.0);
        var pages = new List<ExtractedPage>
        {
            Page(1, ("Physics Grade 11", 11), ("الفصل الأول: القوة", 20), body, body),
            Page(2, ("Physics Grade 11", 11), body, body),
            Page(3, ("Physics Grade 11", 11), body, body),
            Page(4, ("Physics Grade 11", 11), ("الفصل الثاني: الطاقة", 20), body),
            Page(5, ("Physics Grade 11", 11), body, body),
        };
        var sections = StructureDetector.Detect(new ExtractedDocument(5, pages, []));
        Assert.Equal(2, sections.Count);
        Assert.Equal("الفصل الأول: القوة", sections[0].Title);
        Assert.Equal((1, 3), (sections[0].StartPage, sections[0].EndPage));
        Assert.Equal((4, 5), (sections[1].StartPage, sections[1].EndPage));
    }

    [Fact]
    public void Nothing_is_invented_without_evidence()
    {
        var body = ("Uniform body text without any headings at all.", 11.0);
        var pages = Enumerable.Range(1, 4).Select(n => Page(n, body, body)).ToList();
        Assert.Empty(StructureDetector.Detect(new ExtractedDocument(4, pages, [])));
    }
}

public class RtlLineAssemblerTests
{
    [Fact]
    public void Drops_detached_diacritics() =>
        Assert.Equal("تعرف القوة", RtlLineAssembler.Assemble(["تعرف", "ُ", "َّ", "القوة"]));

    [Fact]
    public void Restores_left_to_right_runs_and_trailing_punctuation()
    {
        Assert.Equal("المركبة mg sinθ والمركبة", RtlLineAssembler.Assemble(["المركبة", "sinθ", "mg", "والمركبة"]));
        Assert.Equal("العمودية mg cosθ. والاحتكاك", RtlLineAssembler.Assemble(["العمودية", "cosθ", ".mg", "والاحتكاك"]));
        Assert.Equal("تساوي mgh.", RtlLineAssembler.Assemble(["تساوي", ".mgh"]));
    }

    [Fact]
    public void Fixes_mirrored_brackets_but_keeps_correct_ones()
    {
        Assert.Equal("النيوتن (N)،", RtlLineAssembler.Assemble(["النيوتن", ")N(،"]));
        Assert.Equal("(N)", RtlLineAssembler.FixMirroredBrackets("(N)"));
    }
}

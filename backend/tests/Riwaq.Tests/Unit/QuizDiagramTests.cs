using Riwaq.Application.Features.Quizzes;
using Riwaq.Domain;

namespace Riwaq.Tests.Unit;

public class QuizDiagramTests
{
    private const string Incline = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 260" width="400" height="260">
          <defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="4" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 z"/></marker></defs>
          <polygon points="20,240 380,240 380,60" fill="none" stroke="black"/>
          <line x1="250" y1="150" x2="250" y2="230" stroke="black" marker-end="url(#arrow)"/>
          <text x="255" y="235">(1)</text><text x="300" y="120">30°</text>
        </svg>
        """;

    [Fact]
    public void A_clean_drawing_keeps_its_shapes_and_scales_with_its_container()
    {
        var svg = QuizDiagram.Sanitize(Incline)!;

        Assert.Contains("<polygon", svg);
        Assert.Contains("marker-end=\"url(#arrow)\"", svg);
        Assert.Contains("viewBox=\"0 0 400 260\"", svg);
        Assert.DoesNotContain("width=\"400\"", svg);
        Assert.Contains("30°", svg);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><script>alert(1)</script><rect width=\"5\" height=\"5\"/></svg>", "script")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\" onload=\"alert(1)\"><rect width=\"5\" height=\"5\" onclick=\"x()\"/></svg>", "on")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><foreignObject><div>x</div></foreignObject><rect width=\"5\" height=\"5\"/></svg>", "foreignObject")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\"><a xlink:href=\"javascript:alert(1)\"><rect width=\"5\" height=\"5\"/></a><image href=\"http://evil/x.png\"/><rect width=\"5\" height=\"5\"/></svg>", "href")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\" fill=\"url(http://evil/x)\" style=\"background:url(x)\"/></svg>", "evil")]
    public void Anything_that_can_run_code_or_load_resources_is_removed(string markup, string forbidden)
    {
        var svg = QuizDiagram.Sanitize(markup);
        Assert.NotNull(svg);
        Assert.DoesNotContain(forbidden, svg!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style", svg!);
    }

    [Theory]
    [InlineData("<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><text>&x;</text></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"5\" height=\"5\"/></svg>")]
    [InlineData("<div>not svg</div>")]
    [InlineData("not xml at all")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><title>empty</title></svg>")]
    public void Unusable_or_dangerous_documents_are_rejected(string markup) => Assert.Null(QuizDiagram.Sanitize(markup));

    [Fact]
    public void Labels_and_referenced_markers_are_compared_after_normalising_digits()
    {
        var labels = QuizDiagram.Labels(QuizDiagram.Sanitize(Incline)!);
        Assert.Contains("1", labels);
        Assert.Equal(["1", "س"], QuizDiagram.ReferencedMarkers("ما مقدار القوة (١) والزاوية عند (س)؟"));
    }

    private static RawQuestion Question(string prompt, string? diagram, string? alt = "منحدر مائل بزاوية 30 درجة وعليه السهم رقم 1 يمثل الوزن.",
        List<string>? options = null, int? correct = 0) => new(
        "multiple_choice", prompt, options ?? ["الوزن", "رد الفعل", "الاحتكاك", "الشد"], [], correct, null, [], null, null, null, null, [],
        "السهم المتجه رأسيًا لأسفل يمثل الوزن.", null, "easy", "القوى", 1, "القوة مؤثر خارجي", diagram, alt);

    private static readonly Dictionary<int, string> Pages = new() { [1] = "القوة مؤثر خارجي يغير الحالة الحركية للجسم" };

    [Fact]
    public void A_valid_diagram_question_keeps_its_sanitised_drawing()
    {
        var outcome = QuestionValidator.Validate(Question("ماذا يمثل السهم (1) في الشكل؟", Incline), Pages, new Random(1), requireDiagram: true);

        Assert.Null(outcome.RejectionReason);
        Assert.Contains("<polygon", outcome.Question!.DiagramSvg);
        Assert.NotNull(outcome.Question.DiagramAlt);
    }

    [Fact]
    public void Diagram_questions_are_rejected_when_the_drawing_is_missing_unlabelled_or_gives_the_answer_away()
    {
        Assert.Equal("invalid_diagram", QuestionValidator.Validate(Question("ماذا يمثل السهم (1)؟", null), Pages, new Random(1), true).RejectionReason);
        Assert.Equal("missing_diagram_alt", QuestionValidator.Validate(Question("ماذا يمثل السهم (1)؟", Incline, alt: ""), Pages, new Random(1), true).RejectionReason);
        Assert.Equal("diagram_label_missing", QuestionValidator.Validate(Question("ماذا يمثل السهم (2)؟", Incline), Pages, new Random(1), true).RejectionReason);

        var leaking = Incline.Replace("<text x=\"300\" y=\"120\">30°</text>", "<text x=\"300\" y=\"120\">الوزن</text>");
        Assert.Equal("diagram_reveals_answer", QuestionValidator.Validate(Question("ماذا يمثل السهم (1)؟", leaking), Pages, new Random(1), true).RejectionReason);
    }

    [Fact]
    public void Mixed_difficulty_runs_from_easy_to_hard_and_drawings_are_spread_otherwise()
    {
        ValidatedQuestion Q(string id, Difficulty d) => new(QuestionType.TrueFalse, id, "{}", "{}", "x", null, d, null, null, null);

        var mixed = QuizService.Arrange([Q("t1", Difficulty.Hard), Q("t2", Difficulty.Easy)], [Q("d1", Difficulty.Medium)], Difficulty.Mixed);
        Assert.Equal(["t2", "d1", "t1"], mixed.Select(q => q.Prompt));

        var spread = QuizService.Arrange([Q("t1", 0), Q("t2", 0), Q("t3", 0), Q("t4", 0)], [Q("d1", 0), Q("d2", 0)], Difficulty.Medium);
        Assert.Equal(["t1", "t2", "d1", "t3", "t4", "d2"], spread.Select(q => q.Prompt));
    }
}

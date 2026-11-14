using System.Net;
using System.Text.Json;
using Riwaq.Tests.Support;
using static Riwaq.Tests.Support.ApiTestHelpers;

namespace Riwaq.Tests.Integration;

/// <summary>Exams in the Ministry (Thanaweya Amma) format, and exams copied from a past paper.</summary>
public sealed class MinistryExamTests(RiwaqApiFactory factory) : IClassFixture<RiwaqApiFactory>
{
    private static string Mc(string prompt, string correct, string quote, int page, string difficulty) =>
        $$"""{"type":"multiple_choice","prompt":"{{prompt}}","options":["{{correct}}","Wrong A","Wrong B","Wrong C"],"matchRight":[],"correctIndex":0,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"Because.","hint":null,"difficulty":"{{difficulty}}","topic":"Forces","page":{{page}},"sourceQuote":"{{quote}}","fromPaper":true}""";

    private static readonly string Questions = "{\"questions\":[" + string.Join(",",
        Mc("What is a force?", "A push or a pull", "A force is a push or a pull acting on an object", 1, "easy"),
        Mc("What is the SI unit of force?", "The newton", "The SI unit of force is the newton (N)", 1, "medium"),
        Mc("What does friction oppose?", "Relative motion", "Friction opposes the relative motion between two surfaces", 2, "hard"),
        """{"type":"structured","prompt":"Explain the conservation of energy.","options":[],"matchRight":[],"correctIndex":null,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":"Energy changes form but is never created or destroyed.","rubric":["changes form","not created or destroyed"],"explanation":"Conservation.","hint":null,"difficulty":"medium","topic":"Energy","page":3,"sourceQuote":"Energy is conserved in an isolated system","fromPaper":false}""")
        + "]}";

    [Fact]
    public async Task A_ministry_exam_follows_the_students_template_with_its_marks_and_time()
    {
        factory.Chat.Respond("quiz", _ => Questions);
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();
        var subjectId = (await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync()).GetProperty("book").GetProperty("subjectId").GetInt32();

        var defaults = await (await client.GetAsync("/api/exam-templates")).ReadJsonAsync();
        var physics = defaults.EnumerateArray().First(t => t.GetProperty("subjectId").GetInt32() == subjectId);
        Assert.Equal(44, physics.GetProperty("multipleChoiceCount").GetInt32());   // the Ministry's 2026 science paper
        Assert.Equal(60, physics.GetProperty("totalPoints").GetDouble());
        Assert.True(physics.GetProperty("isDefault").GetBoolean());

        var bad = await client.PutAsync($"/api/exam-templates/{subjectId}", JsonBody(new { multipleChoiceCount = 3, twoMarkChoiceCount = 5, essayCount = 1, essayPoints = 2, durationMinutes = 30, diagramCount = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var saved = await (await client.PutAsync($"/api/exam-templates/{subjectId}",
            JsonBody(new { multipleChoiceCount = 3, twoMarkChoiceCount = 1, essayCount = 1, essayPoints = 2, durationMinutes = 30, diagramCount = 0 }))).ReadJsonAsync();
        Assert.False(saved.GetProperty("isDefault").GetBoolean());

        var quiz = await client.GenerateQuizAsync(new { bookId, ministryFormat = true, count = 10, mode = "practice" });

        var info = quiz.GetProperty("quiz");
        Assert.Equal("ready", info.GetProperty("status").GetString());
        Assert.True(info.GetProperty("ministryFormat").GetBoolean());
        Assert.Equal("exam", info.GetProperty("mode").GetString());
        Assert.Equal(1800, info.GetProperty("timeLimitSeconds").GetInt32());
        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(3, questions.Count(q => q.GetProperty("type").GetString() == "multipleChoice"));
        Assert.Single(questions, q => q.GetProperty("type").GetString() == "structured");
        Assert.Equal(6, info.GetProperty("totalPoints").GetDouble());              // 2 × 1 + 1 × 2 (hardest) + essay 2
        var hard = questions.First(q => q.GetProperty("prompt").GetString() == "What does friction oppose?");
        Assert.Equal(2, hard.GetProperty("points").GetDouble());

        var reset = await (await client.DeleteAsync($"/api/exam-templates/{subjectId}")).ReadJsonAsync();
        Assert.True(reset.GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task A_past_paper_becomes_an_exam_in_its_own_order_with_suggested_answers_marked()
    {
        factory.Chat.Respond("quiz", _ => Questions);
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        var quiz = await client.GenerateQuizAsync(new { bookId, extractFromSource = true, language = "en" });

        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(4, questions.Count);
        Assert.Equal("What is a force?", questions[0].GetProperty("prompt").GetString());   // the paper's order, not by difficulty
        Assert.Equal("exam", quiz.GetProperty("quiz").GetProperty("mode").GetString());
        Assert.Equal("A push or a pull", questions[0].GetProperty("options").GetProperty("choices")[0].GetString()); // the paper's choice order

        var request = factory.Chat.Requests.Last(r => r.Feature == "quiz");
        Assert.Contains("fromPaper", request.JsonSchema);
        Assert.Contains("Never invent questions", request.SystemPrompt);

        // The worked-out answer is labelled as a suggestion in the review after submitting.
        var attempt = await (await client.PostAsync($"/api/quizzes/{quiz.GetProperty("quiz").GetProperty("id").GetGuid()}/attempts", null)).ReadJsonAsync();
        var result = await (await client.PostAsync($"/api/attempts/{attempt.GetProperty("id").GetGuid()}/submit", null)).ReadJsonAsync();
        var text = result.ToString();
        Assert.Contains("Suggested answer", text, StringComparison.OrdinalIgnoreCase);
    }
}

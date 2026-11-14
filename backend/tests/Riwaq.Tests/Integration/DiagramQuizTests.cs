using System.Text.Json;
using Riwaq.Tests.Support;
using static Riwaq.Tests.Support.ApiTestHelpers;

namespace Riwaq.Tests.Integration;

/// <summary>Quizzes with diagram questions and a graded mix of difficulties.</summary>
public sealed class DiagramQuizTests(RiwaqApiFactory factory) : IClassFixture<RiwaqApiFactory>
{
    private const string TextQuestions = """
        {"questions":[
          {"type":"true_false","prompt":"Energy can be created in an isolated system.","options":[],"matchRight":[],"correctIndex":null,"correctBool":false,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"Energy is conserved.","hint":null,"difficulty":"hard","topic":"Energy","page":3,"sourceQuote":"it changes form but is never created or destroyed"},
          {"type":"fill_blank","prompt":"The SI unit of force is the ____.","options":[],"matchRight":[],"correctIndex":null,"correctBool":null,"acceptedAnswers":["newton"],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"The newton.","hint":null,"difficulty":"easy","topic":"Units","page":1,"sourceQuote":"The SI unit of force is the newton"}
        ]}
        """;

    private static string DiagramQuestions(string svg) => JsonSerializer.Serialize(new
    {
        questions = new[]
        {
            new
            {
                type = "multiple_choice", prompt = "In the figure, which force does arrow (1) show on the block on the incline?",
                options = new[] { "Weight", "Normal force", "Friction", "Tension" }, matchRight = Array.Empty<string>(), correctIndex = (int?)0,
                correctBool = (bool?)null, acceptedAnswers = Array.Empty<string>(), numericAnswer = (double?)null, tolerance = (double?)null, unit = (string?)null,
                modelAnswer = (string?)null, rubric = Array.Empty<string>(), explanation = "Arrow (1) points straight down: the weight.", hint = (string?)null,
                difficulty = "medium", topic = "Forces on inclines", page = 2, sourceQuote = "Resolving forces into components is useful on inclined planes",
                diagram = svg, diagramAlt = "A block on an incline at 30 degrees; arrow (1) points vertically down from the block."
            }
        }
    });

    private const string Drawing = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 260" onload="alert(1)"><script>alert(1)</script>
          <polygon points="20,240 380,240 380,60" fill="none" stroke="black"/><rect x="200" y="120" width="40" height="30"/>
          <line x1="220" y1="135" x2="220" y2="230" stroke="black"/><text x="225" y="232">1</text><text x="330" y="230">30°</text></svg>
        """;

    [Fact]
    public async Task A_quiz_is_written_in_the_background_and_a_failed_one_cannot_be_started()
    {
        factory.Chat.Respond("quiz", _ => "{\"questions\":[]}"); // nothing usable comes back
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        var queued = await (await client.PostAsync("/api/quizzes", JsonBody(new { bookId, count = 3 }))).ReadJsonAsync();
        Assert.Equal("generating", queued.GetProperty("quiz").GetProperty("status").GetString());
        Assert.Empty(queued.GetProperty("questions").EnumerateArray());

        var quiz = await client.GenerateQuizAsync(new { bookId, count = 3 });
        Assert.Equal("failed", quiz.GetProperty("quiz").GetProperty("status").GetString());
        Assert.Equal("ai_invalid_output", quiz.GetProperty("quiz").GetProperty("errorCode").GetString());

        var start = await client.PostAsync($"/api/quizzes/{quiz.GetProperty("quiz").GetProperty("id").GetGuid()}/attempts", null);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, start.StatusCode);

        var notifications = await (await client.GetAsync("/api/notifications")).ReadJsonAsync();
        Assert.Contains("quiz_failed", notifications.ToString());
        // Timestamps read back from the database are sent as UTC ("Z"), so the browser does not shift them.
        Assert.EndsWith("Z", notifications[0].GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task Diagram_questions_are_mixed_in_with_a_sanitised_drawing_and_ordered_from_easy_to_hard()
    {
        factory.Chat.Respond("quiz", request => request.JsonSchema!.Contains("\"diagram\"") ? DiagramQuestions(Drawing) : TextQuestions);
        factory.Chat.Respond("quiz_review", _ => "{\"reviews\":[{\"index\":0,\"ok\":true,\"problem\":null}]}");
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        var quiz = await client.GenerateQuizAsync(new { bookId, count = 3, diagramCount = 1, difficulty = "mixed" });

        Assert.Equal("mixed", quiz.GetProperty("quiz").GetProperty("difficulty").GetString());
        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(3, questions.Count);
        Assert.Equal(["easy", "medium", "hard"], questions.Select(q => q.GetProperty("difficulty").GetString()));

        var drawn = Assert.Single(questions, q => q.GetProperty("diagramSvg").ValueKind == JsonValueKind.String);
        var svg = drawn.GetProperty("diagramSvg").GetString()!;
        Assert.Contains("<polygon", svg);
        Assert.DoesNotContain("script", svg);
        Assert.DoesNotContain("onload", svg);
        Assert.Contains("arrow (1)", drawn.GetProperty("diagramAlt").GetString());

        // The answer key is not part of what the student receives.
        Assert.DoesNotContain("correct", drawn.GetProperty("options").GetRawText());
    }

    [Fact]
    public async Task A_drawing_the_review_rejects_is_left_out()
    {
        factory.Chat.Respond("quiz", request => request.JsonSchema!.Contains("\"diagram\"") ? DiagramQuestions(Drawing) : TextQuestions);
        factory.Chat.Respond("quiz_review", _ => "{\"reviews\":[{\"index\":0,\"ok\":false,\"problem\":\"force arrow points the wrong way\"}]}");
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        var quiz = await client.GenerateQuizAsync(new { bookId, count = 3, diagramCount = 1 });

        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.NotEmpty(questions);
        Assert.DoesNotContain(questions, q => q.GetProperty("diagramSvg").ValueKind == JsonValueKind.String);
        var review = factory.Chat.Requests.Last(r => r.Feature == "quiz_review");
        Assert.Contains("<svg>", review.Messages[0].Content);
        Assert.Contains("<correct_answer>", review.Messages[0].Content);
        factory.Chat.Respond("quiz_review", _ => throw new InvalidOperationException("review down")); // later tests: fail open
    }

    [Fact]
    public async Task A_failed_diagram_request_still_leaves_a_quiz_of_text_questions()
    {
        factory.Chat.Respond("quiz", request => request.JsonSchema!.Contains("\"diagram\"") ? "not json" : TextQuestions);
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        var quiz = await client.GenerateQuizAsync(new { bookId, count = 3, diagramCount = 1 });

        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(2, questions.Count);
        Assert.All(questions, q => Assert.Equal(JsonValueKind.Null, q.GetProperty("diagramSvg").ValueKind));
    }
}

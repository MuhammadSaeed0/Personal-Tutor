using System.Net;
using System.Text.Json;
using Riwaq.Tests.Support;
using static Riwaq.Tests.Support.ApiTestHelpers;

namespace Riwaq.Tests.Integration;

/// <summary>The side chat during an exam: unlimited help with meanings, no answers, counted on the result.</summary>
public sealed class ExamAssistantTests(RiwaqApiFactory factory) : IClassFixture<RiwaqApiFactory>
{
    private const string Quiz = """
        {"questions":[
          {"type":"multiple_choice","prompt":"According to Newton's second law, the net force equals what?","options":["Mass times acceleration","Mass divided by velocity","Weight times height","Power times time"],"matchRight":[],"correctIndex":0,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"SECRET-EXPLANATION F = m a.","hint":null,"difficulty":"easy","topic":"Newton's second law","page":1,"sourceQuote":"net force equals mass times acceleration"}
        ]}
        """;

    [Fact]
    public async Task The_assistant_explains_without_the_answer_key_counts_every_use_and_stops_after_submission()
    {
        factory.Chat.Respond("quiz", _ => Quiz);
        factory.Chat.Respond("exam_assistant", _ => "Acceleration is the rate of change of velocity [1].");
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();
        var quiz = await client.GenerateQuizAsync(new { bookId, count = 1, mode = "exam", timeLimitMinutes = 30 });
        var questionId = quiz.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var attemptId = (await (await client.PostAsync($"/api/quizzes/{quiz.GetProperty("quiz").GetProperty("id").GetGuid()}/attempts", null)).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        for (var i = 1; i <= 2; i++)
        {
            var events = await SseReader.ReadAllAsync(await client.PostAsync($"/api/attempts/{attemptId}/assistant",
                JsonBody(new { question = "What does acceleration mean?", questionId, language = "en" })));
            var done = Assert.Single(events, e => e.Type == "done");
            Assert.Equal(i, done.Data.GetProperty("uses").GetInt32());
            Assert.Contains("rate of change", done.Data.GetProperty("content").GetString());
        }

        var request = factory.Chat.Requests.Last(r => r.Feature == "exam_assistant");
        Assert.Contains("Never give away an exam answer", request.SystemPrompt);
        var turn = request.Messages[^1].Content;
        Assert.Contains("<current_exam_question>", turn);
        Assert.Contains("Mass times acceleration", turn);         // the choices are shown for context …
        Assert.DoesNotContain("SECRET-EXPLANATION", turn);        // … but nothing from the answer key or explanation
        Assert.DoesNotContain("\"correct\"", turn);

        var submitted = await client.PostAsync($"/api/attempts/{attemptId}/submit", null);
        Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
        var after = await client.PostAsync($"/api/attempts/{attemptId}/assistant", JsonBody(new { question = "And now?", questionId }));
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
        Assert.Equal("attempt_finished", JsonDocument.Parse(await after.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

        var result = await (await client.GetAsync($"/api/attempts/{attemptId}/result")).ReadJsonAsync();
        Assert.Equal(2, result.GetProperty("assistantUses").GetInt32());
    }

    [Fact]
    public async Task A_reply_that_gives_the_answer_away_is_withheld()
    {
        factory.Chat.Respond("quiz", _ => Quiz);
        factory.Chat.Respond("exam_assistant", _ => "The net force equals mass times acceleration, so pick that choice.");
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();
        var quiz = await client.GenerateQuizAsync(new { bookId, count = 1, mode = "exam", timeLimitMinutes = 30 });
        var questionId = quiz.GetProperty("questions")[0].GetProperty("id").GetGuid();
        var attemptId = (await (await client.PostAsync($"/api/quizzes/{quiz.GetProperty("quiz").GetProperty("id").GetGuid()}/attempts", null)).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var events = await SseReader.ReadAllAsync(await client.PostAsync($"/api/attempts/{attemptId}/assistant",
            JsonBody(new { question = "Which one is it?", questionId, language = "en" })));
        Assert.DoesNotContain(events, e => e.Type == "delta");          // nothing reaches the student before the check
        var content = Assert.Single(events, e => e.Type == "done").Data.GetProperty("content").GetString()!;
        Assert.DoesNotContain("mass times acceleration", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("can't give or confirm", content);
    }

    [Fact]
    public async Task Another_student_cannot_use_the_assistant_on_someone_elses_attempt()
    {
        factory.Chat.Respond("quiz", _ => Quiz);
        var owner = await factory.CreateStudentAsync();
        var bookId = await owner.UploadAndProcessAsync();
        var quiz = await owner.GenerateQuizAsync(new { bookId, count = 1 });
        var attemptId = (await (await owner.PostAsync($"/api/quizzes/{quiz.GetProperty("quiz").GetProperty("id").GetGuid()}/attempts", null)).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var intruder = await factory.CreateStudentAsync();
        var response = await intruder.PostAsync($"/api/attempts/{attemptId}/assistant", JsonBody(new { question = "help" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Riwaq.Application.Abstractions;
using Riwaq.Tests.Support;
using static Riwaq.Tests.Support.ApiTestHelpers;

namespace Riwaq.Tests.Integration;

/// <summary>The mistakes notebook: wrong answers come back on a schedule until answered correctly twice in a row.</summary>
public sealed class MistakesTests(RiwaqApiFactory factory) : IClassFixture<RiwaqApiFactory>
{
    private const string Quiz = """
        {"questions":[
          {"type":"multiple_choice","prompt":"According to Newton's second law, the net force equals what?","options":["Mass times acceleration","Mass divided by velocity","Weight times height","Power times time"],"matchRight":[],"correctIndex":0,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"F = m a.","hint":null,"difficulty":"easy","topic":"Newton's second law","page":1,"sourceQuote":"net force equals mass times acceleration"}
        ]}
        """;

    private static async Task AnswerAndSubmitAsync(HttpClient client, Guid quizId, bool correct)
    {
        var attempt = await (await client.PostAsync($"/api/quizzes/{quizId}/attempts", null)).ReadJsonAsync();
        var question = attempt.GetProperty("quiz").GetProperty("questions")[0];
        var choices = question.GetProperty("options").GetProperty("choices").EnumerateArray().Select(c => c.GetString()).ToList();
        var index = correct ? choices.IndexOf("Mass times acceleration") : choices.FindIndex(c => c != "Mass times acceleration");
        var attemptId = attempt.GetProperty("id").GetGuid();
        (await client.PutAsync($"/api/attempts/{attemptId}/responses/{question.GetProperty("id").GetGuid()}", JsonBody(new { answer = index }))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/attempts/{attemptId}/submit", null)).EnsureSuccessStatusCode();
    }

    private async Task MakeAllDueAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Mistakes.ExecuteUpdateAsync(s => s.SetProperty(m => m.NextReviewAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public async Task A_wrong_answer_is_reviewed_without_ai_until_answered_correctly_twice()
    {
        factory.Chat.Respond("quiz", _ => Quiz);
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();
        var quiz = await client.GenerateQuizAsync(new { bookId, count = 1, language = "en" });
        var subjectId = quiz.GetProperty("quiz").GetProperty("subjectId").GetInt32();
        await AnswerAndSubmitAsync(client, quiz.GetProperty("quiz").GetProperty("id").GetGuid(), correct: false);

        var usage = await (await client.GetAsync("/api/account/ai-usage?days=14")).ReadJsonAsync();
        Assert.Equal(14, usage.GetProperty("daily").GetArrayLength());
        Assert.True(usage.GetProperty("todayRequests").GetInt32() >= 1);
        Assert.Contains(usage.GetProperty("byFeature").EnumerateArray(), f => f.GetProperty("feature").GetString() == "quiz");

        var notebook = await (await client.GetAsync("/api/mistakes")).ReadJsonAsync();
        var item = Assert.Single(notebook.GetProperty("items").EnumerateArray());
        Assert.Equal("Mass times acceleration", item.GetProperty("correctAnswer").GetString());
        Assert.False(item.GetProperty("due").GetBoolean());          // comes back tomorrow
        Assert.Equal(0, notebook.GetProperty("dueCount").GetInt32());

        var none = await client.PostAsync("/api/mistakes/retest", JsonBody(new { subjectId }));
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);

        await MakeAllDueAsync();
        var today = await (await client.GetAsync("/api/today")).ReadJsonAsync();
        Assert.Equal(1, today.GetProperty("mistakesDue").GetInt32());
        Assert.Equal(subjectId, today.GetProperty("mistakesBySubject")[0].GetProperty("subjectId").GetInt32());

        // Two correct reviews in a row master it; review quizzes copy the question and make no AI call.
        for (var round = 1; round <= 2; round++)
        {
            await MakeAllDueAsync();
            var aiCalls = factory.Chat.Requests.Count;
            var retest = await (await client.PostAsync("/api/mistakes/retest", JsonBody(new { subjectId }))).ReadJsonAsync();
            Assert.Equal(1, retest.GetProperty("questionCount").GetInt32());
            await AnswerAndSubmitAsync(client, retest.GetProperty("quizId").GetGuid(), correct: true);
            Assert.Equal(aiCalls, factory.Chat.Requests.Count);
        }

        var after = await (await client.GetAsync("/api/mistakes?includeMastered=true")).ReadJsonAsync();
        Assert.Equal(0, after.GetProperty("openCount").GetInt32());
        Assert.Equal(1, after.GetProperty("masteredCount").GetInt32());
        Assert.Equal(JsonValueKind.String, after.GetProperty("items")[0].GetProperty("masteredAt").ValueKind);

        var other = await factory.CreateStudentAsync();
        var foreign = await other.DeleteAsync($"/api/mistakes/{after.GetProperty("items")[0].GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }
}

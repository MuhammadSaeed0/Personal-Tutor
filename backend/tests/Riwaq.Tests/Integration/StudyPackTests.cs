using System.Net;
using System.Text.Json;
using Riwaq.Tests.Support;
using static Riwaq.Tests.Support.ApiTestHelpers;

namespace Riwaq.Tests.Integration;

/// <summary>The study pack: a summary, flashcards and a quiz per chapter, made in the background after processing.</summary>
public sealed class StudyPackTests(RiwaqApiFactory factory) : IClassFixture<RiwaqApiFactory>
{
    private const string Quiz = """
        {"questions":[
          {"type":"multiple_choice","prompt":"According to Newton's second law, the net force equals what?","options":["Mass times acceleration","Mass divided by velocity","Weight times height","Power times time"],"matchRight":[],"correctIndex":0,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"F = m a.","hint":null,"difficulty":"easy","topic":"Newton's second law","page":1,"sourceQuote":"net force equals mass times acceleration"}
        ]}
        """;

    [Fact]
    public async Task Each_chapter_gets_a_summary_cards_and_a_quiz()
    {
        factory.Chat.Respond("summary", _ => "- A force is a push or a pull [p:1]");
        factory.Chat.Respond("flashcards", _ => """{"cards":[{"front":"State Newton's second law","back":"$F = ma$","page":1,"tags":["Newton"]}]}""");
        factory.Chat.Respond("quiz", _ => Quiz);
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/books/{bookId}/study-pack")).StatusCode);
        var pack = await (await client.PostAsync($"/api/books/{bookId}/study-pack", null)).ReadJsonAsync();
        Assert.Equal("pending", pack.GetProperty("status").GetString());

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (pack.GetProperty("status").GetString() is "pending" or "running" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(300);
            pack = await (await client.GetAsync($"/api/books/{bookId}/study-pack")).ReadJsonAsync();
        }

        Assert.Equal("completed", pack.GetProperty("status").GetString());
        var units = pack.GetProperty("total").GetInt32();
        Assert.True(units >= 1);
        Assert.Equal(units, pack.GetProperty("done").GetInt32());
        Assert.Equal(units, pack.GetProperty("summaries").GetInt32());
        Assert.Equal(units, pack.GetProperty("quizzes").GetInt32());
        Assert.True(pack.GetProperty("cards").GetInt32() >= 1);   // the same card text is not saved twice

        var summaries = await (await client.GetAsync($"/api/summaries?bookId={bookId}")).ReadJsonAsync();
        Assert.True(summaries.ValueKind == JsonValueKind.Array ? summaries.GetArrayLength() >= 1 : summaries.GetProperty("items").GetArrayLength() >= 1);
        var notifications = await (await client.GetAsync("/api/notifications")).ReadJsonAsync();
        Assert.Contains("study_pack_ready", notifications.ToString());

        var other = await factory.CreateStudentAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/books/{bookId}/study-pack", null)).StatusCode);
    }
}

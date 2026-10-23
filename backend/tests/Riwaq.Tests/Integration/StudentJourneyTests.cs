using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Riwaq.Application.Abstractions;
using Riwaq.Tests.Support;
using static Riwaq.Tests.Support.ApiTestHelpers;

namespace Riwaq.Tests.Integration;

/// <summary>
/// The complete journey from the definition of done, run against the real API, database, PDF pipeline,
/// retrieval, grading and scheduling. Only the LLM is scripted.
/// </summary>
[Collection(nameof(ApiCollection))]
public partial class StudentJourneyTests(RiwaqApiFactory factory)
{
    [Fact]
    public async Task Full_study_journey_from_upload_to_updated_progress()
    {
        ScriptAi();

        // 1–3. Register, upload a textbook and wait for processing.
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();

        // 4–5. Open the book and navigate to page 1 (reading heartbeat long enough to count the page).
        var detail = await (await client.GetAsync($"/api/books/{bookId}")).ReadJsonAsync();
        var chapter1 = detail.GetProperty("chapters")[0].GetProperty("id").GetGuid();
        var progress = await (await client.PutAsync($"/api/books/{bookId}/progress", JsonBody(new { page = 1, secondsOnPage = 45 }))).ReadJsonAsync();
        Assert.Contains(1, progress.GetProperty("readPages").EnumerateArray().Select(p => p.GetInt32()));
        await client.PutAsync($"/api/books/{bookId}/progress", JsonBody(new { page = 2, secondsOnPage = 40 }));

        // 6–7. Ask the tutor about a passage; the answer carries citations that map to real pages.
        var conversation = await (await client.PostAsync("/api/tutor/conversations", JsonBody(new { bookId }))).ReadJsonAsync();
        var conversationId = conversation.GetProperty("id").GetGuid();
        var events = await SseReader.ReadAllAsync(await client.PostAsync($"/api/tutor/conversations/{conversationId}/messages",
            JsonBody(new { question = "What does Newton's second law say?", currentPage = 1, selectedText = "net force equals mass times acceleration" })));

        Assert.Equal("start", events[0].Type);
        Assert.Contains(events, e => e.Type == "delta");
        var done = events.Single(e => e.Type == "done").Data.GetProperty("message");
        var citations = done.GetProperty("citations").EnumerateArray().ToList();
        Assert.Single(citations);                                     // the fabricated [9] marker was dropped
        Assert.Equal(1, citations[0].GetProperty("pageStart").GetInt32());
        Assert.Contains("second law", citations[0].GetProperty("quote").GetString());
        Assert.DoesNotContain("[9]", done.GetProperty("content").GetString());
        Assert.Equal(2, done.GetProperty("followUps").GetArrayLength());
        Assert.True(done.GetProperty("grounded").GetBoolean());

        // The prompt kept textbook text inside the untrusted block and the selected text was passed explicitly.
        var tutorRequest = factory.Chat.Requests.Last(r => r.Feature == "tutor");
        Assert.Contains("untrusted", tutorRequest.SystemPrompt);
        Assert.Contains("<selected_text page=\"1\">", tutorRequest.Messages[^1].Content);

        var history = await (await client.GetAsync($"/api/tutor/conversations/{conversationId}/messages")).ReadJsonAsync();
        Assert.Equal(2, history.GetArrayLength());

        // 8. Generate and save a summary of chapter 1; invalid page citations are removed.
        var summary = await (await client.PostAsync("/api/summaries", JsonBody(new { bookId, scope = "chapter", style = "keyPoints", chapterId = chapter1 }))).ReadJsonAsync();
        var summaryPages = summary.GetProperty("citations").EnumerateArray().Select(c => c.GetProperty("pageStart").GetInt32()).ToList();
        Assert.Equal([1, 2], summaryPages);
        Assert.DoesNotContain("p:99", summary.GetProperty("content").GetString());
        var edited = await (await client.PatchAsync($"/api/summaries/{summary.GetProperty("id").GetGuid()}", JsonBody(new { content = "My edited summary [p:1]" }))).ReadJsonAsync();
        Assert.True(edited.GetProperty("isEdited").GetBoolean());

        // 9. Create flashcards from the chapter (AI drafts → saved) and review one.
        var drafts = await (await client.PostAsync("/api/flashcards/generate", JsonBody(new { bookId, scope = "chapter", chapterId = chapter1, count = 5 }))).ReadJsonAsync();
        var draftList = drafts.GetProperty("drafts").EnumerateArray().ToList();
        Assert.Equal(2, draftList.Count);
        Assert.Equal(JsonValueKind.Null, draftList[1].GetProperty("page").ValueKind); // page 77 is not in the source
        var saved = await (await client.PostAsync("/api/flashcards/batch", JsonBody(new
        {
            cards = draftList.Select(d => new { bookId, chapterId = chapter1, front = d.GetProperty("front").GetString(), back = d.GetProperty("back").GetString(), sourceType = "aiGenerated" })
        }))).ReadJsonAsync();
        var cardId = saved[0].GetProperty("id").GetGuid();
        var due = await (await client.GetAsync("/api/flashcards/due")).ReadJsonAsync();
        Assert.Equal(2, due.GetProperty("cards").GetArrayLength());
        var reviewed = await (await client.PostAsync($"/api/flashcards/{cardId}/review", JsonBody(new { rating = "good" }))).ReadJsonAsync();
        Assert.Equal("learning", reviewed.GetProperty("card").GetProperty("state").GetString());

        // 10. Generate and complete a quiz.
        var quiz = await (await client.PostAsync("/api/quizzes", JsonBody(new
        {
            bookId, count = 10, types = new[] { "multipleChoice", "trueFalse", "numerical", "shortAnswer", "fillBlank" }, revealImmediately = false, language = "en"
        }))).ReadJsonAsync();
        var questions = quiz.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(5, questions.Count);                                        // unsupported + ambiguous questions were excluded
        Assert.Equal(2, quiz.GetProperty("quiz").GetProperty("excludedQuestionCount").GetInt32());
        Assert.DoesNotContain("correct", quiz.ToString(), StringComparison.Ordinal); // no answer keys before submission

        var attempt = await (await client.PostAsync($"/api/quizzes/{quiz.GetProperty("quiz").GetProperty("id").GetGuid()}/attempts", null)).ReadJsonAsync();
        var attemptId = attempt.GetProperty("id").GetGuid();
        foreach (var q in questions)
        {
            object answer = q.GetProperty("type").GetString() switch
            {
                "multipleChoice" => q.GetProperty("options").GetProperty("choices").EnumerateArray().Select((c, i) => (c, i)).First(x => x.c.GetString() == "Mass times acceleration").i,
                "trueFalse" => true,                     // deliberately wrong (correct answer is false)
                "numerical" => "9.8",
                "fillBlank" => "newton",
                _ => "Energy changes form but the total stays constant."
            };
            var save = await client.PutAsync($"/api/attempts/{attemptId}/responses/{q.GetProperty("id").GetGuid()}", JsonBody(new { answer }));
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        }

        // 11. Submit and review results with explanations and source pages.
        var result = await (await client.PostAsync($"/api/attempts/{attemptId}/submit", null)).ReadJsonAsync();
        Assert.Equal(4, result.GetProperty("correctCount").GetInt32());
        Assert.Equal(1, result.GetProperty("incorrectCount").GetInt32());
        Assert.Equal(80, result.GetProperty("percent").GetDouble());
        Assert.Contains(3, result.GetProperty("reviewPages").EnumerateArray().Select(p => p.GetInt32()));
        var wrong = result.GetProperty("questions").EnumerateArray().Single(q => q.GetProperty("response").GetProperty("isCorrect").ValueKind == JsonValueKind.False);
        Assert.Equal("False", wrong.GetProperty("correctAnswer").GetString());
        Assert.False(string.IsNullOrEmpty(wrong.GetProperty("explanation").GetString()));
        var openAnswer = result.GetProperty("questions").EnumerateArray().Single(q => q.GetProperty("question").GetProperty("type").GetString() == "shortAnswer");
        Assert.Equal("aiRubric", openAnswer.GetProperty("response").GetProperty("gradingMethod").GetString());

        var mistakeCards = await (await client.PostAsync($"/api/flashcards/from-attempt/{attemptId}", null)).ReadJsonAsync();
        Assert.Equal(1, mistakeCards.GetArrayLength());

        // 12. Progress reflects real activity.
        var overview = await (await client.GetAsync("/api/analytics/overview")).ReadJsonAsync();
        Assert.True(overview.GetProperty("hasData").GetBoolean());
        Assert.Equal(1, overview.GetProperty("quizzesTaken30d").GetInt32());
        Assert.Equal(80, overview.GetProperty("quizAccuracy30d").GetDouble());
        Assert.Equal(1, overview.GetProperty("currentStreak").GetInt32());

        var topics = await (await client.GetAsync("/api/analytics/topics")).ReadJsonAsync();
        var energy = topics.EnumerateArray().Single(t => t.GetProperty("topic").GetString() == "Energy conservation");
        Assert.Equal(0, energy.GetProperty("questionCorrect").GetInt32());
        Assert.Equal(3, energy.GetProperty("lastMistakePage").GetInt32());

        var dashboard = await (await client.GetAsync("/api/dashboard")).ReadJsonAsync();
        Assert.True(dashboard.GetProperty("hasBooks").GetBoolean());
        Assert.Contains("first_quiz", dashboard.GetProperty("achievements").EnumerateArray().Select(a => a.GetProperty("code").GetString()));
        Assert.True(dashboard.GetProperty("today").GetProperty("tasks").GetArrayLength() > 0);
        var physics = dashboard.GetProperty("subjects").EnumerateArray().Single(s => s.GetProperty("subjectId").GetInt32() == 4);
        Assert.Equal(1, physics.GetProperty("books").GetInt32());
        Assert.Equal(2, physics.GetProperty("pagesRead").GetInt32());
        Assert.Equal(1, physics.GetProperty("chaptersCompleted").GetInt32()); // pages 1–2 read → chapter 1 complete
    }

    [Fact]
    public async Task Tutor_failure_is_reported_and_saved_without_fake_answer()
    {
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();
        var conversation = await (await client.PostAsync("/api/tutor/conversations", JsonBody(new { bookId }))).ReadJsonAsync();
        var id = conversation.GetProperty("id").GetGuid();

        factory.Chat.Unavailable = true;
        try
        {
            var events = await SseReader.ReadAllAsync(await client.PostAsync($"/api/tutor/conversations/{id}/messages", JsonBody(new { question = "Explain page 2" })));
            var error = events.Single(e => e.Type == "error").Data;
            Assert.Equal("ai_unavailable", error.GetProperty("code").GetString());

            var messages = await (await client.GetAsync($"/api/tutor/conversations/{id}/messages")).ReadJsonAsync();
            Assert.Equal("error", messages[1].GetProperty("status").GetString());
            Assert.Equal("", messages[1].GetProperty("content").GetString());

            var summary = await client.PostAsync("/api/summaries", JsonBody(new { bookId, scope = "page", style = "quick", page = 1 }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, summary.StatusCode);
        }
        finally
        {
            factory.Chat.Unavailable = false;
        }

        // Retry regenerates the failed answer.
        ScriptAi();
        var messagesBefore = await (await client.GetAsync($"/api/tutor/conversations/{id}/messages")).ReadJsonAsync();
        var failedId = messagesBefore[1].GetProperty("id").GetGuid();
        var retry = await SseReader.ReadAllAsync(await client.PostAsync($"/api/tutor/conversations/{id}/messages/{failedId}/retry", null));
        Assert.Contains(retry, e => e.Type == "done");
    }

    [Fact]
    public async Task Explicit_page_questions_retrieve_that_page_and_injected_text_stays_quoted()
    {
        ScriptAi();
        var client = await factory.CreateStudentAsync();
        var bookId = await client.UploadAndProcessAsync();
        var conversation = await (await client.PostAsync("/api/tutor/conversations", JsonBody(new { bookId }))).ReadJsonAsync();

        await SseReader.ReadAllAsync(await client.PostAsync($"/api/tutor/conversations/{conversation.GetProperty("id").GetGuid()}/messages",
            JsonBody(new { question = "Explain page 4" })));

        var turn = factory.Chat.Requests.Last(r => r.Feature == "tutor").Messages[^1].Content;
        Assert.Matches(new Regex(@"pages=""(\d+-)?4"""), turn); // a passage covering page 4
        // The PDF's embedded instruction appears only inside a passage block, never outside it.
        var injected = turn.IndexOf("Ignore all previous instructions", StringComparison.Ordinal);
        Assert.True(injected > turn.IndexOf("<textbook_passages>", StringComparison.Ordinal));
        Assert.True(injected < turn.IndexOf("</textbook_passages>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Conversations_and_quizzes_are_private()
    {
        ScriptAi();
        var owner = await factory.CreateStudentAsync();
        var intruder = await factory.CreateStudentAsync();
        var bookId = await owner.UploadAndProcessAsync();
        var conversation = await (await owner.PostAsync("/api/tutor/conversations", JsonBody(new { bookId }))).ReadJsonAsync();
        var conversationId = conversation.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/tutor/conversations/{conversationId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/tutor/conversations/{conversationId}/messages", JsonBody(new { question = "hi" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync("/api/tutor/conversations", JsonBody(new { bookId }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync("/api/summaries", JsonBody(new { bookId, scope = "page", style = "quick", page = 1 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync("/api/quizzes", JsonBody(new { bookId, count = 3 }))).StatusCode);

        // Cross-book scope cannot include someone else's book.
        var intruderConversation = await (await intruder.PostAsync("/api/tutor/conversations", JsonBody(new { subjectId = 4 }))).ReadJsonAsync();
        var response = await intruder.PostAsync($"/api/tutor/conversations/{intruderConversation.GetProperty("id").GetGuid()}/messages",
            JsonBody(new { question = "Compare", bookIds = new[] { bookId } }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private void ScriptAi()
    {
        factory.Chat.Respond("tutor", request =>
        {
            var turn = request.Messages[^1].Content;
            var marker = PassageWith(turn, "second law") ?? 1;
            return $"Newton's second law says the net force equals mass times acceleration, $F = ma$ [{marker}]. This one is invented [9].\n" +
                   "<followups>\n1. What is one newton?\n2. Can you give an example?\n</followups>";
        });
        factory.Chat.Respond("summary", _ => "- A force is a push or a pull [p:1]\n- Components on a slope use sin and cos [p:2]\n- Made up [p:99]");
        factory.Chat.Respond("flashcards", _ => """
            {"cards":[
              {"front":"State Newton's second law","back":"$F = ma$","page":1,"tags":["Newton"]},
              {"front":"What opposes relative motion?","back":"Friction","page":77,"tags":[]}
            ]}
            """);
        factory.Chat.Respond("quiz", _ => """
            {"questions":[
              {"type":"multiple_choice","prompt":"According to Newton's second law, the net force equals what?","options":["Mass times acceleration","Mass divided by velocity","Weight times height","Power times time"],"matchRight":[],"correctIndex":0,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"F = m a.","hint":"Think F = ?","difficulty":"easy","topic":"Newton's second law","page":1,"sourceQuote":"net force equals mass times acceleration"},
              {"type":"true_false","prompt":"Energy can be created in an isolated system.","options":[],"matchRight":[],"correctIndex":null,"correctBool":false,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"Energy is conserved; it is never created or destroyed.","hint":null,"difficulty":"easy","topic":"Energy conservation","page":3,"sourceQuote":"it changes form but is never created or destroyed"},
              {"type":"numerical","prompt":"What is g in m/s² used in PE = m g h (Earth)?","options":[],"matchRight":[],"correctIndex":null,"correctBool":null,"acceptedAnswers":[],"numericAnswer":9.81,"tolerance":0.02,"unit":"m/s²","modelAnswer":null,"rubric":[],"explanation":"g ≈ 9.81 m/s².","hint":null,"difficulty":"medium","topic":"Potential energy","page":3,"sourceQuote":"Potential energy near the Earth's surface is PE = m g h"},
              {"type":"fill_blank","prompt":"The SI unit of force is the ____.","options":[],"matchRight":[],"correctIndex":null,"correctBool":null,"acceptedAnswers":["newton","N"],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"The newton (N).","hint":null,"difficulty":"easy","topic":"Units","page":1,"sourceQuote":"The SI unit of force is the newton"},
              {"type":"short_answer","prompt":"State the law of conservation of energy.","options":[],"matchRight":[],"correctIndex":null,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":"Energy is conserved in an isolated system; it changes form but is never created or destroyed.","rubric":["changes form","not created or destroyed"],"explanation":"See page 3.","hint":null,"difficulty":"medium","topic":"Energy conservation law","page":3,"sourceQuote":"Energy is conserved in an isolated system"},
              {"type":"multiple_choice","prompt":"What colour is the textbook cover?","options":["Red","Blue","Green"],"matchRight":[],"correctIndex":1,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"Invented fact.","hint":null,"difficulty":"easy","topic":"Trivia","page":2,"sourceQuote":"The cover of this book is bright blue with stars"},
              {"type":"multiple_choice","prompt":"Which is a unit of power?","options":["Watt","watt","Joule"],"matchRight":[],"correctIndex":0,"correctBool":null,"acceptedAnswers":[],"numericAnswer":null,"tolerance":null,"unit":null,"modelAnswer":null,"rubric":[],"explanation":"The watt.","hint":null,"difficulty":"easy","topic":"Power","page":4,"sourceQuote":"Power is the rate of doing work, measured in watts"}
            ]}
            """);
        factory.Chat.Respond("grading", _ => """{"results":[{"id":1,"score":1,"feedback":"Correct and complete.","misconception":null}]}""");
    }

    private static int? PassageWith(string turn, string text)
    {
        foreach (Match m in PassageRegex().Matches(turn))
            if (m.Groups[2].Value.Contains(text, StringComparison.OrdinalIgnoreCase)) return int.Parse(m.Groups[1].Value);
        return null;
    }

    [GeneratedRegex("<passage id=\"(\\d+)\"[^>]*>(.*?)</passage>", RegexOptions.Singleline)]
    private static partial Regex PassageRegex();
}

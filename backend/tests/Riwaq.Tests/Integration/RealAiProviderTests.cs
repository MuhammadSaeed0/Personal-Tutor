using Microsoft.Extensions.Logging.Abstractions;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Features.Ai;
using Riwaq.Infrastructure.Ai;

namespace Riwaq.Tests.Integration;

/// <summary>Runs only when RIWAQ_TEST_ANTHROPIC_KEY is set. Calls the real Claude API (costs a few cents).</summary>
public sealed class RealAiFactAttribute : FactAttribute
{
    public RealAiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RIWAQ_TEST_ANTHROPIC_KEY")))
            Skip = "Set RIWAQ_TEST_ANTHROPIC_KEY (and optionally RIWAQ_TEST_ANTHROPIC_MODEL) to test against the real provider.";
    }
}

/// <summary>Runs only when RIWAQ_TEST_CLAUDE_CLI=1. Uses the signed-in Claude account through the Claude Code CLI.</summary>
public sealed class ClaudeAccountFactAttribute : FactAttribute
{
    public ClaudeAccountFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RIWAQ_TEST_CLAUDE_CLI") != "1")
            Skip = "Set RIWAQ_TEST_CLAUDE_CLI=1 (and optionally RIWAQ_TEST_CLAUDE_MODEL) to test with your Claude account.";
    }
}

public class RealAiProviderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static AnthropicChatModel ApiModel() => new(new ChatProviderOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("RIWAQ_TEST_ANTHROPIC_KEY"),
        Model = Environment.GetEnvironmentVariable("RIWAQ_TEST_ANTHROPIC_MODEL") ?? "claude-opus-5-5",
        Effort = "low"
    }, NullLogger<AnthropicChatModel>.Instance);

    private static ClaudeCodeChatModel AccountModel() => new(new ChatProviderOptions
    {
        Provider = "ClaudeCode",
        Model = Environment.GetEnvironmentVariable("RIWAQ_TEST_CLAUDE_MODEL") ?? "sonnet",
        Effort = "low"
    }, NullLogger<ClaudeCodeChatModel>.Instance);

    [RealAiFact]
    public Task Api_streams_an_answer_that_cites_supplied_passages() => StreamsCitedAnswer(ApiModel());

    [RealAiFact]
    public Task Api_returns_schema_valid_json_for_structured_output() => ReturnsSchemaValidJson(ApiModel());

    [ClaudeAccountFact]
    public Task Account_streams_an_answer_that_cites_supplied_passages() => StreamsCitedAnswer(AccountModel());

    [ClaudeAccountFact]
    public Task Account_returns_schema_valid_json_for_structured_output() => ReturnsSchemaValidJson(AccountModel());

    [ClaudeAccountFact]
    public async Task Account_replays_earlier_turns()
    {
        var result = await AccountModel().CompleteAsync(new ChatRequest
        {
            Feature = "tutor",
            SystemPrompt = "Answer in one short sentence.",
            Messages =
            [
                new ChatTurn(ChatRole.User, "My favourite element is neon. Remember it."),
                new ChatTurn(ChatRole.Assistant, "Noted: your favourite element is neon."),
                new ChatTurn(ChatRole.User, "Which element did I say is my favourite?")
            ],
            MaxTokens = 200
        }, CancellationToken.None);
        Assert.Contains("neon", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads one scanned page through the account: set RIWAQ_TEST_SCANNED_PDF (and optionally RIWAQ_TEST_SCANNED_PAGE).</summary>
    [ClaudeAccountFact]
    public async Task Account_reads_a_scanned_page()
    {
        var pdf = Environment.GetEnvironmentVariable("RIWAQ_TEST_SCANNED_PDF");
        if (string.IsNullOrEmpty(pdf)) return;
        var pageNumber = int.Parse(Environment.GetEnvironmentVariable("RIWAQ_TEST_SCANNED_PAGE") ?? "1");
        var engine = new Riwaq.Infrastructure.Pdf.AiVisionOcrEngine(AccountModel(),
            Microsoft.Extensions.Options.Options.Create(new Riwaq.Infrastructure.Pdf.OcrOptions()),
            NullLogger<Riwaq.Infrastructure.Pdf.AiVisionOcrEngine>.Instance);

        var result = await engine.RecognizePageAsync(pdf, pageNumber, "ara+eng", CancellationToken.None);

        output.WriteLine($"confidence={result.MeanConfidence} error={result.ErrorCode}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), $"riwaq-ocr-page-{pageNumber}.txt"), result.Text);
        Assert.True(result.Success, result.ErrorCode);
    }

    private static async Task StreamsCitedAnswer(IChatModel model)
    {
        var request = new ChatRequest
        {
            Feature = "tutor",
            SystemPrompt = Prompts.Tutor("physics", "Physics", "Test book", "en", false, "concise"),
            Messages = [new ChatTurn(ChatRole.User, Prompts.TutorUserTurn("What does Newton's second law say?", null, "en", null, 1, null,
                [new Application.Features.Retrieval.RetrievedPassage(1, Guid.NewGuid(), "Test book", null, null, 12, 12,
                    "Newton's second law states that the net force equals mass times acceleration, F = m a.", 1, "keyword")], false))],
            MaxTokens = 800
        };
        var text = new System.Text.StringBuilder();
        ChatResult? final = null;
        await foreach (var chunk in model.StreamAsync(request, CancellationToken.None))
        {
            text.Append(chunk.TextDelta);
            final ??= chunk.Final;
        }
        Assert.Contains("[1]", text.ToString());
        Assert.NotNull(final);
        Assert.True(final!.OutputTokens > 0);
    }

    private static async Task ReturnsSchemaValidJson(IChatModel model)
    {
        var result = await model.CompleteAsync(new ChatRequest
        {
            Feature = "quiz",
            SystemPrompt = "Return JSON only.",
            Messages = [new ChatTurn(ChatRole.User, "Give one flashcard about photosynthesis.")],
            JsonSchema = """{"type":"object","properties":{"front":{"type":"string"},"back":{"type":"string"}},"required":["front","back"],"additionalProperties":false}""",
            MaxTokens = 400
        }, CancellationToken.None);
        using var doc = System.Text.Json.JsonDocument.Parse(result.Text);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("front").GetString()));
    }
}

/// <summary>Runs only when RIWAQ_TEST_YOUTUBE is set to a video id. Reads real YouTube metadata and captions.</summary>
public sealed class RealYouTubeFactAttribute : FactAttribute
{
    public RealYouTubeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RIWAQ_TEST_YOUTUBE")))
            Skip = "Set RIWAQ_TEST_YOUTUBE to a public video id (and optionally RIWAQ_TEST_YOUTUBE_LANG) to read real captions.";
    }
}

public class RealYouTubeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [RealYouTubeFact]
    public async Task Reads_details_and_captions_of_a_public_video()
    {
        var id = Environment.GetEnvironmentVariable("RIWAQ_TEST_YOUTUBE")!;
        var source = new Riwaq.Infrastructure.Media.YouTubeTranscriptSource(new HttpClient(),
            NullLogger<Riwaq.Infrastructure.Media.YouTubeTranscriptSource>.Instance);

        var details = await source.GetDetailsAsync(id, CancellationToken.None);
        var transcript = await source.GetTranscriptAsync(id, Environment.GetEnvironmentVariable("RIWAQ_TEST_YOUTUBE_LANG") ?? "ar", CancellationToken.None);

        Assert.NotNull(transcript);
        var windows = Riwaq.Application.Ingestion.TranscriptSegmenter.Segment(transcript!.Captions);
        output.WriteLine($"{details.Title} | {details.Author} | {details.DurationSeconds}s | lang={transcript.Language} auto={transcript.IsAutoGenerated} captions={transcript.Captions.Count} windows={windows.Count}");
        output.WriteLine(windows[0].Text[..Math.Min(300, windows[0].Text.Length)]);
        Assert.NotEmpty(windows);
    }
}

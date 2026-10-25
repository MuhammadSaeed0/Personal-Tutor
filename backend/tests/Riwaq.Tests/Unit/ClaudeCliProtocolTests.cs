using Riwaq.Application.Abstractions;
using Riwaq.Infrastructure.Ai;

namespace Riwaq.Tests.Unit;

public class ClaudeCliProtocolTests
{
    [Fact]
    public void Arguments_disable_every_tool_and_local_customisation()
    {
        var args = ClaudeCliProtocol.BuildArguments("sonnet", "High", null, "sys.txt", streaming: true);

        var tools = args.ToList().IndexOf("--tools");
        Assert.True(tools >= 0);
        Assert.Equal("", args[tools + 1]);
        Assert.Equal("", args[args.ToList().IndexOf("--setting-sources") + 1]);
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.Contains("--include-partial-messages", args);
        Assert.Equal("high", args[args.ToList().IndexOf("--effort") + 1]);
        Assert.Equal("sys.txt", args[args.ToList().IndexOf("--system-prompt-file") + 1]);
        Assert.DoesNotContain("--json-schema", args);
    }

    [Fact]
    public void Arguments_pass_schema_and_skip_unknown_effort()
    {
        var args = ClaudeCliProtocol.BuildArguments("", "extreme", """{"type":"object"}""", "sys.txt", streaming: false);

        Assert.Equal("""{"type":"object"}""", args[args.ToList().IndexOf("--json-schema") + 1]);
        Assert.DoesNotContain("--effort", args);
        Assert.DoesNotContain("--model", args);
        Assert.DoesNotContain("--include-partial-messages", args);
    }

    [Fact]
    public void Multi_line_schema_is_passed_on_one_line()
    {
        var args = ClaudeCliProtocol.BuildArguments("sonnet", null, Riwaq.Infrastructure.Pdf.AiVisionOcrEngine.Schema, "sys.txt", streaming: false);

        var schema = args[args.ToList().IndexOf("--json-schema") + 1];
        Assert.DoesNotContain('\n', schema);
        Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(Riwaq.Infrastructure.Pdf.AiVisionOcrEngine.Schema)!.ToJsonString(), schema);
    }

    [Fact]
    public void Single_turn_prompt_is_sent_verbatim_and_history_is_labelled()
    {
        Assert.Equal("سؤال", ClaudeCliProtocol.BuildPrompt([new ChatTurn(ChatRole.User, "سؤال")]));

        var prompt = ClaudeCliProtocol.BuildPrompt(
        [
            new ChatTurn(ChatRole.User, "first"),
            new ChatTurn(ChatRole.Assistant, "answer"),
            new ChatTurn(ChatRole.User, "second")
        ]);
        Assert.True(prompt.IndexOf("[Student]\nfirst", StringComparison.Ordinal) < prompt.IndexOf("[You]\nanswer", StringComparison.Ordinal));
        Assert.EndsWith("second", prompt);
    }

    [Fact]
    public void Parses_text_deltas_model_and_result_usage()
    {
        Assert.Equal(new ClaudeCliEvent.MessageStart("claude-sonnet-5-5"), ClaudeCliProtocol.ParseLine(
            """{"type":"stream_event","event":{"type":"message_start","message":{"model":"claude-sonnet-5-5","usage":{"input_tokens":2}}}}"""));
        Assert.Equal(new ClaudeCliEvent.TextDelta("أهلاً"), ClaudeCliProtocol.ParseLine(
            """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"أهلاً"}}}"""));
        Assert.Null(ClaudeCliProtocol.ParseLine(
            """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{"}}}"""));
        Assert.Null(ClaudeCliProtocol.ParseLine("""{"type":"system","subtype":"init","tools":[]}"""));
        Assert.Null(ClaudeCliProtocol.ParseLine("not json"));

        var result = ClaudeCliProtocol.ParseLine(
            """{"type":"result","subtype":"success","is_error":false,"result":"Hi","stop_reason":"end_turn","usage":{"input_tokens":2,"cache_creation_input_tokens":900,"cache_read_input_tokens":100,"output_tokens":13}}""");
        Assert.Equal(new ClaudeCliEvent.Result("Hi", 1002, 13, "end_turn"), result);
    }

    [Fact]
    public void Structured_output_wins_over_result_text()
    {
        var result = Assert.IsType<ClaudeCliEvent.Result>(ClaudeCliProtocol.ParseLine(
            """{"type":"result","subtype":"success","is_error":false,"result":"{\"city\":\"Paris\"}","structured_output":{"city":"Paris"},"stop_reason":"tool_use","usage":{"input_tokens":1,"output_tokens":5}}"""));
        Assert.Equal("""{"city":"Paris"}""", result.Text);
    }

    [Theory]
    [InlineData("""{"type":"result","subtype":"success","is_error":true,"result":"Claude AI usage limit reached|1792741800"}""", "ai_rate_limited")]
    [InlineData("""{"type":"result","subtype":"success","is_error":true,"result":"Not logged in · Please run /login"}""", "ai_not_configured")]
    [InlineData("""{"type":"result","subtype":"error_during_execution","is_error":true}""", "ai_unavailable")]
    [InlineData("""{"type":"result","subtype":"success","is_error":false,"result":"","stop_reason":"refusal"}""", "ai_refused")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected"}}""", "ai_rate_limited")]
    public void Errors_map_to_application_codes(string line, string code)
    {
        var failure = Assert.IsType<ClaudeCliEvent.Failure>(ClaudeCliProtocol.ParseLine(line));
        Assert.Equal(code, failure.Code);
    }

    [Fact]
    public void Allowed_rate_limit_events_are_ignored()
    {
        Assert.Null(ClaudeCliProtocol.ParseLine("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed"}}"""));
    }

    [Fact]
    public void Missing_executable_resolves_to_null()
    {
        Assert.Null(ClaudeCliProtocol.ResolveExecutable("riwaq-no-such-cli-" + Guid.NewGuid().ToString("N")));
        Assert.Null(ClaudeCliProtocol.ResolveExecutable(""));
    }
}

public class VisionOcrTests
{
    [Fact]
    public void Image_input_is_one_stream_json_user_message_with_image_then_text()
    {
        var line = ClaudeCliProtocol.BuildImageInput("اقرأ الصفحة", [new ChatImage("image/jpeg", [1, 2, 3])]);

        Assert.EndsWith("\n", line);
        using var doc = System.Text.Json.JsonDocument.Parse(line);
        var content = doc.RootElement.GetProperty("message").GetProperty("content");
        Assert.Equal("user", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        Assert.Equal("AQID", content[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("image/jpeg", content[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("اقرأ الصفحة", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Image_requests_switch_the_cli_to_json_input()
    {
        var args = ClaudeCliProtocol.BuildArguments("sonnet", null, null, "sys.txt", streaming: false, jsonInput: true);
        Assert.Equal("stream-json", args[args.ToList().IndexOf("--input-format") + 1]);
    }

    [Fact]
    public void Transcription_and_figure_descriptions_become_page_text()
    {
        var result = Riwaq.Infrastructure.Pdf.AiVisionOcrEngine.ParseResult(
            """{"text":"الصخور النارية تتكون من تبلور الصهارة.","figures":[{"caption":"شكل (2)","description":"قطاع في بركان"},{"caption":"","description":"صورة لصخر الجرانيت"}],"legibility":"medium"}""",
            "ara+eng");

        Assert.True(result.Success);
        Assert.Equal("الصخور النارية تتكون من تبلور الصهارة.\n\n[شكل: شكل (2) — قطاع في بركان]\n\n[شكل: صورة لصخر الجرانيت]", result.Text);
        Assert.Equal(75, result.MeanConfidence);
    }

    [Theory]
    [InlineData("""{"text":"","figures":[],"legibility":"high"}""", "ocr_no_text")]
    [InlineData("not json", "ocr_failed")]
    [InlineData("""{"text":"x"}""", "ocr_failed")]
    public void Empty_or_invalid_output_is_not_a_successful_read(string json, string code)
    {
        var result = Riwaq.Infrastructure.Pdf.AiVisionOcrEngine.ParseResult(json, "eng");
        Assert.False(result.Success);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public void Low_legibility_is_reported_below_the_warning_threshold()
    {
        var result = Riwaq.Infrastructure.Pdf.AiVisionOcrEngine.ParseResult("""{"text":"Page","figures":[],"legibility":"low"}""", "eng");
        Assert.True(result.MeanConfidence < 60);
    }
}

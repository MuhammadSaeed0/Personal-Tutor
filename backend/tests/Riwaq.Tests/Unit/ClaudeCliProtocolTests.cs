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

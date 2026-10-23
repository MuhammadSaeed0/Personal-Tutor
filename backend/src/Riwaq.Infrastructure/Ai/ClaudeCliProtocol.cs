using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Ai;

/// <summary>An event parsed from one line of <c>claude -p --output-format stream-json</c>.</summary>
internal abstract record ClaudeCliEvent
{
    public sealed record MessageStart(string Model) : ClaudeCliEvent;
    public sealed record TextDelta(string Text) : ClaudeCliEvent;
    public sealed record Result(string Text, int InputTokens, int OutputTokens, string? StopReason) : ClaudeCliEvent;
    public sealed record Failure(string Code, string Message) : ClaudeCliEvent;
}

/// <summary>Pure helpers for driving the Claude Code CLI (argument list, prompt, output parsing).</summary>
internal static class ClaudeCliProtocol
{
    public static IReadOnlyList<string> BuildArguments(string model, string? effort, string? jsonSchema, string systemPromptFile, bool streaming)
    {
        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--system-prompt-file", systemPromptFile,
            // Text-only: no built-in tools, MCP servers, settings files (hooks, plugins), slash commands or saved sessions.
            "--tools", "",
            "--setting-sources", "",
            "--strict-mcp-config",
            "--safe-mode",
            "--disable-slash-commands",
            "--no-session-persistence"
        };
        if (streaming) args.Add("--include-partial-messages");
        if (!string.IsNullOrWhiteSpace(model)) args.AddRange(["--model", model]);
        if (effort?.ToLowerInvariant() is "low" or "medium" or "high") args.AddRange(["--effort", effort.ToLowerInvariant()]);
        if (jsonSchema is not null) args.AddRange(["--json-schema", jsonSchema]);
        return args;
    }

    /// <summary>The CLI takes a single prompt, so earlier turns are replayed as a labelled transcript.</summary>
    public static string BuildPrompt(IReadOnlyList<ChatTurn> messages)
    {
        if (messages.Count == 1) return messages[0].Content;

        var prompt = new StringBuilder("Earlier messages in this conversation, for context:\n\n");
        foreach (var turn in messages.Take(messages.Count - 1))
        {
            prompt.Append(turn.Role == ChatRole.User ? "[Student]\n" : "[You]\n").Append(turn.Content.Trim()).Append("\n\n");
        }
        prompt.Append("---\nThe student's new message, which you are answering now:\n\n").Append(messages[^1].Content);
        return prompt.ToString();
    }

    public static ClaudeCliEvent? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return null;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { return null; }
        if (node is not JsonObject obj) return null;

        switch (Str(obj["type"]))
        {
            case "stream_event":
                var ev = obj["event"];
                return Str(ev?["type"]) switch
                {
                    "message_start" when Str(ev?["message"]?["model"]) is { } model => new ClaudeCliEvent.MessageStart(model),
                    "content_block_delta" when Str(ev?["delta"]?["type"]) == "text_delta" && Str(ev?["delta"]?["text"]) is { Length: > 0 } text
                        => new ClaudeCliEvent.TextDelta(text),
                    _ => null
                };

            case "rate_limit_event" when Str(obj["rate_limit_info"]?["status"]) == "rejected":
                return new ClaudeCliEvent.Failure("ai_rate_limited", "Your Claude plan's usage limit has been reached. Try again after it resets.");

            case "result":
                return ParseResult(obj);

            default:
                return null;
        }
    }

    private static ClaudeCliEvent ParseResult(JsonObject obj)
    {
        var text = obj["structured_output"] is { } structured && structured.GetValueKind() != JsonValueKind.Null
            ? structured.ToJsonString()
            : Str(obj["result"]) ?? string.Empty;
        var stopReason = Str(obj["stop_reason"]);

        if (obj["is_error"]?.GetValueKind() == JsonValueKind.True || Str(obj["subtype"]) is { } subtype && subtype != "success")
            return new ClaudeCliEvent.Failure(ClassifyError(text), "The Claude CLI could not complete the request.");
        if (stopReason == "refusal")
            return new ClaudeCliEvent.Failure("ai_refused", "The AI provider declined this request.");

        var usage = obj["usage"];
        var input = Int(usage?["input_tokens"]) + Int(usage?["cache_creation_input_tokens"]) + Int(usage?["cache_read_input_tokens"]);
        return new ClaudeCliEvent.Result(text, input, Int(usage?["output_tokens"]), stopReason);
    }

    /// <summary>Maps CLI error text to the application's AI error codes.</summary>
    public static string ClassifyError(string? message)
    {
        var m = message?.ToLowerInvariant() ?? string.Empty;
        if (m.Contains("limit") && (m.Contains("usage") || m.Contains("rate") || m.Contains("reached") || m.Contains("hit your")))
            return "ai_rate_limited";
        if (m.Contains("log in") || m.Contains("login") || m.Contains("not logged") || m.Contains("authenticat") || m.Contains("api key") || m.Contains("oauth"))
            return "ai_not_configured";
        return "ai_unavailable";
    }

    /// <summary>Finds the CLI on PATH (honouring PATHEXT on Windows) or in the native installer's default folder.</summary>
    public static string? ResolveExecutable(string cliPath)
    {
        if (string.IsNullOrWhiteSpace(cliPath)) return null;
        if (Path.IsPathRooted(cliPath)) return File.Exists(cliPath) ? cliPath : null;

        string[] extensions = OperatingSystem.IsWindows() && !Path.HasExtension(cliPath)
            ? [".exe", .. (Environment.GetEnvironmentVariable("PATHEXT") ?? ".CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)]
            : [""];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));

        foreach (var directory in directories)
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory.Trim('"'), cliPath + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? Str(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
    private static int Int(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : 0;
}

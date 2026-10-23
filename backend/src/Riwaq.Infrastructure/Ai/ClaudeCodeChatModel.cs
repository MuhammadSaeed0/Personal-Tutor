using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;

namespace Riwaq.Infrastructure.Ai;

/// <summary>
/// Claude through the locally installed Claude Code CLI, authenticated with the machine owner's own Claude account
/// (claude.ai sign-in, e.g. with Google). Intended for a personal, self-hosted deployment: the account belongs to
/// whoever runs the server, so a deployment serving other people must use an API key provider instead.
/// The CLI runs headless with every tool, MCP server, setting file, hook and plugin disabled, so the model can only
/// answer with text: untrusted textbook content can never make it read files or run commands.
/// </summary>
public sealed class ClaudeCodeChatModel : IChatModel
{
    private const int MaxStderrChars = 4000;

    private readonly ChatProviderOptions _options;
    private readonly ILogger<ClaudeCodeChatModel> _logger;
    private readonly string? _executable;
    private readonly string _workDirectory;
    private readonly SemaphoreSlim _concurrency;

    public ClaudeCodeChatModel(ChatProviderOptions options, ILogger<ClaudeCodeChatModel> logger)
    {
        _options = options;
        _logger = logger;
        _executable = ClaudeCliProtocol.ResolveExecutable(options.CliPath);
        _concurrency = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));

        // An empty directory: the CLI never sees the server's files or a project CLAUDE.md.
        _workDirectory = Path.Combine(Path.GetTempPath(), "riwaq-claude-cli");
        Directory.CreateDirectory(_workDirectory);

        if (_executable is null)
            _logger.LogWarning("Claude Code CLI '{CliPath}' was not found; AI features are disabled", options.CliPath);
    }

    public bool IsConfigured => _executable is not null;
    public string ModelName => _options.Model;
    public string Provider => "claude-account";

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ClaudeCliEvent.Result? result = null;
        var model = _options.Model;
        await foreach (var ev in RunAsync(request, streaming: false, cancellationToken))
        {
            if (ev is ClaudeCliEvent.MessageStart start) model = start.Model;
            else if (ev is ClaudeCliEvent.Result r) result = r;
        }

        if (result is null)
            throw new AiUnavailableException("ai_unavailable", "The Claude CLI ended without a result.");
        return new ChatResult(result.Text, result.InputTokens, result.OutputTokens, model, result.StopReason);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var model = _options.Model;
        var emittedText = false;
        await foreach (var ev in RunAsync(request, streaming: true, cancellationToken))
        {
            switch (ev)
            {
                case ClaudeCliEvent.MessageStart start:
                    model = start.Model;
                    break;
                case ClaudeCliEvent.TextDelta delta:
                    emittedText = true;
                    yield return new ChatStreamChunk(delta.Text, null);
                    break;
                case ClaudeCliEvent.Result result:
                    // Structured output arrives as a tool call rather than text deltas.
                    if (!emittedText && result.Text.Length > 0) yield return new ChatStreamChunk(result.Text, null);
                    yield return new ChatStreamChunk(null, new ChatResult(string.Empty, result.InputTokens, result.OutputTokens, model, result.StopReason));
                    yield break;
            }
        }
        throw new AiUnavailableException("ai_unavailable", "The Claude CLI ended without a result.");
    }

    private async IAsyncEnumerable<ClaudeCliEvent> RunAsync(ChatRequest request, bool streaming, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_executable is null)
            throw new AiUnavailableException("ai_not_configured", "Claude Code is not installed on the server. Install it and sign in with `claude auth login`.");

        await _concurrency.WaitAsync(cancellationToken);
        var systemPromptFile = Path.Combine(_workDirectory, $"system-{Guid.NewGuid():N}.txt");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        Process? process = null;
        try
        {
            await File.WriteAllTextAsync(systemPromptFile, request.SystemPrompt, new UTF8Encoding(false), cancellationToken);
            process = Start(request, streaming, systemPromptFile);
            var stderr = ReadBoundedAsync(process.StandardError);

            // The prompt goes through stdin: no command-line length limit and no shell quoting of untrusted text.
            await process.StandardInput.WriteAsync(ClaudeCliProtocol.BuildPrompt(request.Messages).AsMemory(), timeout.Token);
            process.StandardInput.Close();

            var sawResult = false;
            while (true)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new AiUnavailableException("ai_unavailable", "The Claude CLI did not answer in time.");
                }
                if (line is null) break;

                var ev = ClaudeCliProtocol.ParseLine(line);
                switch (ev)
                {
                    case null:
                        continue;
                    case ClaudeCliEvent.Failure failure:
                        _logger.LogWarning("Claude CLI request for {Feature} failed: {Code}", request.Feature, failure.Code);
                        throw new AiUnavailableException(failure.Code, failure.Message);
                    case ClaudeCliEvent.Result:
                        sawResult = true;
                        break;
                }
                yield return ev;
                if (sawResult) yield break;
            }

            await process.WaitForExitAsync(timeout.Token);
            var errors = await stderr;
            _logger.LogWarning("Claude CLI exited with code {ExitCode} without a result: {Stderr}", process.ExitCode, errors);
            throw new AiUnavailableException(ClaudeCliProtocol.ClassifyError(errors), "The Claude CLI failed. Check that you are signed in with `claude auth login`.");
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            TryDelete(systemPromptFile);
            _concurrency.Release();
        }
    }

    private Process Start(ChatRequest request, bool streaming, string systemPromptFile)
    {
        var psi = new ProcessStartInfo(_executable!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _workDirectory,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in ClaudeCliProtocol.BuildArguments(_options.Model, request.Effort ?? _options.Effort, request.JsonSchema, systemPromptFile, streaming))
            psi.ArgumentList.Add(arg);

        // Use the signed-in Claude account, never an API key that happens to be in the server's environment.
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        psi.Environment.Remove("CLAUDECODE");
        psi.Environment["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = request.MaxTokens.ToString(CultureInfo.InvariantCulture);

        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not start the Claude CLI");
            throw new AiUnavailableException("ai_not_configured", "The Claude CLI could not be started.", ex);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new StringBuilder();
        var chunk = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length < MaxStderrChars) buffer.Append(chunk, 0, Math.Min(read, MaxStderrChars - buffer.Length));
        }
        return buffer.ToString();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

namespace Riwaq.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string Section = "Ai";

    public ChatProviderOptions Chat { get; set; } = new();
    public EmbeddingProviderOptions Embeddings { get; set; } = new();
}

public sealed class ChatProviderOptions
{
    /// <summary>
    /// "Anthropic" (API key), "ClaudeCode" (the server owner's Claude account through the Claude Code CLI),
    /// or "None".
    /// </summary>
    public string Provider { get; set; } = "Anthropic";
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Read from configuration/secret store only (environment variable Ai__Chat__ApiKey). Never commit it.</summary>
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Default effort for tutoring answers ("low", "medium", "high").</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Anthropic only: re-serve declined requests on Anthropic's recommended fallback model.</summary>
    public bool UseServerSideFallback { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>ClaudeCode only: CLI command or full path to the executable.</summary>
    public string CliPath { get; set; } = "claude";

    /// <summary>ClaudeCode only: CLI processes allowed to run at the same time.</summary>
    public int MaxConcurrency { get; set; } = 2;
}

public sealed class EmbeddingProviderOptions
{
    /// <summary>
    /// "OnnxE5": semantic search with multilingual-e5-small running on this machine (falls back to "Local" while the
    /// model files are missing). "Local": built-in lexical hashing, nothing to download.
    /// </summary>
    public string Provider { get; set; } = "OnnxE5";

    /// <summary>Folder holding the OnnxE5 model files (scripts/get-model.ps1 downloads them).</summary>
    public string ModelPath { get; set; } = "App_Data/models/multilingual-e5-small";
}

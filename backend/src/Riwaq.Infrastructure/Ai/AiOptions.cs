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
    /// "OpenAICompatible" or "None".
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
    /// <summary>"Local" (built-in lexical hashing, no key needed), "Voyage" or "OpenAICompatible".</summary>
    public string Provider { get; set; } = "Local";
    public string Model { get; set; } = "voyage-3.5";
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public int Dimensions { get; set; } = 1024;
    public int BatchSize { get; set; } = 64;
}

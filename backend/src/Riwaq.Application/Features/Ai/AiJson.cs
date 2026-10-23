using System.Text.Json;
using Riwaq.Application.Abstractions;

namespace Riwaq.Application.Features.Ai;

/// <summary>Parses JSON produced by a model (structured output), tolerating code fences and leading prose.</summary>
public static class AiJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static T Parse<T>(string text) where T : class
    {
        var json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = json.IndexOf('\n');
            json = firstNewline > 0 ? json[(firstNewline + 1)..] : json;
            var fence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) json = json[..fence];
        }
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) throw new AiUnavailableException("ai_invalid_output", "The AI response could not be understood.");
        try
        {
            return JsonSerializer.Deserialize<T>(json[start..(end + 1)], Options)
                   ?? throw new AiUnavailableException("ai_invalid_output", "The AI response was empty.");
        }
        catch (JsonException ex)
        {
            throw new AiUnavailableException("ai_invalid_output", "The AI response could not be understood.", ex);
        }
    }
}

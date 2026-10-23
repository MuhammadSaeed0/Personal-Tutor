using System.Text.Json;

namespace Riwaq.Tests.Support;

public sealed record SseEvent(string Type, JsonElement Data);

public static class SseReader
{
    public static async Task<List<SseEvent>> ReadAllAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{(int)response.StatusCode}: {body}");
        var events = new List<SseEvent>();
        foreach (var block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? type = null, data = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("event: ")) type = line[7..];
                else if (line.StartsWith("data: ")) data = line[6..];
            }
            if (type is not null && data is not null) events.Add(new SseEvent(type, JsonDocument.Parse(data).RootElement.Clone()));
        }
        return events;
    }
}

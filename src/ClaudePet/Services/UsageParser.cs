using System.Text.Json;
using ClaudePet.Models;

namespace ClaudePet.Services;

public static class UsageParser
{
    private const int DefaultContextLimit = 200_000;

    private static readonly Dictionary<string, int> ContextLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-8"] = 200_000,
        ["claude-sonnet-5"] = 200_000,
        ["claude-haiku-4-5"] = 200_000,
    };

    public static UsageSnapshot? TryParseLine(string line, Action<string>? onUnknownModel = null)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("message", out var message))
                return null;
            if (!message.TryGetProperty("usage", out var usage))
                return null;

            int inputTokens = GetInt(usage, "input_tokens");
            int cacheCreation = GetInt(usage, "cache_creation_input_tokens");
            int cacheRead = GetInt(usage, "cache_read_input_tokens");
            int contextTokens = inputTokens + cacheCreation + cacheRead;

            string? model = message.TryGetProperty("model", out var m) ? m.GetString() : null;

            int limit = DefaultContextLimit;
            if (model is not null)
            {
                if (ContextLimits.TryGetValue(model, out var knownLimit))
                    limit = knownLimit;
                else
                    onUnknownModel?.Invoke(model);
            }

            double percent = limit == 0 ? 0 : Math.Clamp(contextTokens / (double)limit * 100.0, 0, 100);

            return new UsageSnapshot(contextTokens, limit, percent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static UsageSnapshot? ParseLatest(IEnumerable<string> lines, Action<string>? onUnknownModel = null)
    {
        UsageSnapshot? latest = null;
        foreach (var line in lines)
        {
            var parsed = TryParseLine(line, onUnknownModel);
            if (parsed is not null)
                latest = parsed;
        }
        return latest;
    }

    private static int GetInt(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var value) ? value.GetInt32() : 0;
}

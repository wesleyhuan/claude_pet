using System.Text.Json;
using ClaudePet.Models;

namespace ClaudePet.Services;

public static class UsageParser
{
    // Real session logs on this machine show observed context tokens of 562,832
    // (claude-opus-4-8) and 379,436 (claude-sonnet-5) — both blow past the old
    // 200,000 hardcoded limit, saturating Math.Clamp at 100% for ordinary
    // sessions. Raised to realistic values based on those observations.
    private const int DefaultContextLimit = 1_000_000;

    private static readonly Dictionary<string, int> ContextLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-8"] = 1_000_000,
        ["claude-sonnet-5"] = 1_000_000,
        ["claude-opus-5"] = 1_000_000,
        // No haiku entry: no real haiku session data has been observed on this
        // machine to validate a limit against, and guessing a hardcoded number
        // risks the exact same "wrong hardcoded limit saturates at 100%" bug this
        // table exists to avoid. An unrecognized haiku line correctly falls
        // through to the safe DefaultContextLimit below instead.
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

            // Interruptions/API errors emit synthetic lines (message.model == "<synthetic>")
            // with all three token fields at zero. A real assistant turn is never zero
            // context, so treat an all-zero reading as "not a real usage snapshot" rather
            // than a legitimate 0% reading — otherwise it gets picked as "latest" by
            // ParseLatest and incorrectly flips the pet to Happy/0%.
            if (inputTokens == 0 && cacheCreation == 0 && cacheRead == 0)
                return null;

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
        catch (Exception)
        {
            // Catch all exceptions: non-object usage/message fields throw InvalidOperationException,
            // non-numeric token fields throw InvalidOperationException or FormatException, etc.
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

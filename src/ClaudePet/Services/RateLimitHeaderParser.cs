using ClaudePet.Models;

namespace ClaudePet.Services;

public static class RateLimitHeaderParser
{
    // Header names are not confirmed against a live API response (no
    // ANTHROPIC_API_KEY was available during design/implementation). These
    // follow Anthropic's documented "anthropic-ratelimit-*" convention as the
    // most likely real names; RateLimitReader (Task 3) logs the full raw
    // header set on first response so a user with a real key can verify.
    private static readonly string[] RemainingHeaderCandidates =
    {
        "anthropic-ratelimit-tokens-remaining",
        "anthropic-ratelimit-input-tokens-remaining",
    };

    private static readonly string[] LimitHeaderCandidates =
    {
        "anthropic-ratelimit-tokens-limit",
        "anthropic-ratelimit-input-tokens-limit",
    };

    private static readonly string[] ResetHeaderCandidates =
    {
        "anthropic-ratelimit-tokens-reset",
        "anthropic-ratelimit-input-tokens-reset",
    };

    public static RateLimitSnapshot Parse(IReadOnlyDictionary<string, string> headers)
    {
        int? remaining = FindInt(headers, RemainingHeaderCandidates);
        int? limit = FindInt(headers, LimitHeaderCandidates);
        DateTimeOffset? resetsAt = FindResetTime(headers, ResetHeaderCandidates);

        double? percent = null;
        if (remaining is int r && limit is int l && l > 0)
        {
            percent = Math.Clamp((l - r) / (double)l * 100.0, 0, 100);
        }

        return new RateLimitSnapshot(remaining, limit, percent, resetsAt);
    }

    private static int? FindInt(IReadOnlyDictionary<string, string> headers, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (headers.TryGetValue(name, out var value) && int.TryParse(value, out var parsed))
                return parsed;
        }
        return null;
    }

    private static DateTimeOffset? FindResetTime(IReadOnlyDictionary<string, string> headers, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (!headers.TryGetValue(name, out var value))
                continue;

            if (DateTimeOffset.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsedDate))
            {
                return parsedDate;
            }

            if (int.TryParse(value, out var secondsFromNow))
                return DateTimeOffset.UtcNow.AddSeconds(secondsFromNow);
        }
        return null;
    }
}

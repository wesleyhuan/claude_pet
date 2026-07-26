using ClaudePet.Models;

namespace ClaudePet.Tray;

public static class TooltipFormatter
{
    // NotifyIcon.Text throws if assigned a string longer than this.
    public const int MaxLength = 63;

    public static string Format(UsageSnapshot? usage, RateLimitSnapshot? rateLimit)
    {
        var line1 = FormatUsageLine(usage);
        var line2 = FormatRateLimitLine(rateLimit);

        if (line2 is null)
            return Truncate(line1, MaxLength);

        var combined = $"{line1}\n{line2}";
        if (combined.Length <= MaxLength)
            return combined;

        // Truncate the rate-limit line first: the session-usage line is the
        // established primary signal.
        var budget = MaxLength - line1.Length - 1; // -1 for the newline
        if (budget <= 0)
            return Truncate(line1, MaxLength);

        return $"{line1}\n{Truncate(line2, budget)}";
    }

    private static string FormatUsageLine(UsageSnapshot? usage) =>
        usage is null
            ? "Claude Pet: no active session"
            : $"Claude Pet: {usage.Percent:F0}% ({usage.ContextTokens:N0}/{usage.ContextLimit:N0})";

    private static string? FormatRateLimitLine(RateLimitSnapshot? rateLimit)
    {
        if (rateLimit is null || rateLimit.Percent is null)
            return null;

        var resetPart = rateLimit.ResetsAt is { } resetsAt
            ? $", resets {FormatRelative(resetsAt)}"
            : "";
        return $"Rate limit: {rateLimit.Percent:F0}% used{resetPart}";
    }

    private static string FormatRelative(DateTimeOffset resetsAt)
    {
        var delta = resetsAt - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)
            return "soon";
        return delta.TotalHours >= 1
            ? $"in {(int)delta.TotalHours}h {delta.Minutes}m"
            : $"in {Math.Max(1, delta.Minutes)}m";
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] : text;
}

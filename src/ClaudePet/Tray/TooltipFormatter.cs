using ClaudePet.Models;

namespace ClaudePet.Tray;

public static class TooltipFormatter
{
    // NotifyIcon.Text throws if assigned a string longer than this.
    public const int MaxLength = 63;

    public static string Format(UsageSnapshot? usage, RateLimitSnapshot? rateLimit, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var line1 = FormatUsageLine(usage);
        var line2 = FormatRateLimitLine(rateLimit, reference);

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

    private static string? FormatRateLimitLine(RateLimitSnapshot? rateLimit, DateTimeOffset now)
    {
        if (rateLimit is null || rateLimit.Percent is null)
            return null;

        var resetPart = rateLimit.ResetsAt is { } resetsAt
            ? $", {FormatRelative(resetsAt, now)}"
            : "";
        return $"Rate limit: {rateLimit.Percent:F0}% used{resetPart}";
    }

    // Deliberately compact (single unit, no combined "Xh Ym"): the 63-char
    // NotifyIcon.Text budget leaves very little room for this clause once a
    // real session-usage line is present (see whole-branch review finding
    // #1 for the arithmetic). A reset window can span up to ~7 days, so
    // days/hours/minutes are each shown alone, never combined.
    private static string FormatRelative(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var delta = resetsAt - now;
        if (delta <= TimeSpan.Zero)
            return "soon";
        if (delta.TotalHours >= 24)
            return $"{(int)delta.TotalDays}d";
        if (delta.TotalHours >= 1)
            return $"{(int)delta.TotalHours}h";
        return $"{Math.Max(1, delta.Minutes)}m";
    }

    // Ellipsis instead of a blind character cut: a bare cut can leave a
    // fragment that reads as a real (wrong) value, e.g. a truncated number.
    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        if (maxLength <= 1)
            return text[..maxLength];
        return text[..(maxLength - 1)] + "…";
    }
}

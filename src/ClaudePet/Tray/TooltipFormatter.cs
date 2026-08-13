using ClaudePet.Models;

namespace ClaudePet.Tray;

public static class TooltipFormatter
{
    // NotifyIcon.Text throws if assigned a string longer than this.
    public const int MaxLength = 63;

    public static string Format(
        UsageSnapshot? usage,
        RateLimitSnapshot? rateLimit,
        SubscriptionUsageSnapshot? subscriptionUsage = null,
        DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var line1 = FormatUsageLine(usage);
        // Subscription usage (opt-in, real account data) takes priority over
        // the API-key header-based rate-limit line when present; falls back
        // to the header-based line when subscription data isn't available
        // (feature disabled, not yet polled, or currently failing).
        var line2 = FormatSubscriptionLine(subscriptionUsage, reference) ?? FormatRateLimitLine(rateLimit, reference);

        if (line2 is null)
            return Truncate(line1, MaxLength);

        var combined = $"{line1}\n{line2}";
        if (combined.Length <= MaxLength)
            return combined;

        // Truncate the second line first: the session-usage line is the
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

    private static string? FormatSubscriptionLine(SubscriptionUsageSnapshot? subscriptionUsage, DateTimeOffset now)
    {
        if (subscriptionUsage is null)
            return null;

        // The tooltip has a 63-char budget shared with line 1, so it shows
        // only whichever window is more constrained (higher utilization) -
        // the pet-window badge is where both windows are shown side by
        // side. On an exact tie, prefer the 5-hour window - it resets
        // sooner and is what Claude Code's own status line surfaces first.
        var fiveHourPercent = subscriptionUsage.FiveHourPercent;
        var weeklyPercent = subscriptionUsage.WeeklyPercent;

        if (fiveHourPercent is null && weeklyPercent is null)
            return null;

        string label;
        double percent;
        DateTimeOffset? resetsAt;
        if (weeklyPercent is null || (fiveHourPercent is not null && fiveHourPercent >= weeklyPercent))
        {
            label = "5h";
            percent = fiveHourPercent!.Value;
            resetsAt = subscriptionUsage.FiveHourResetsAt;
        }
        else
        {
            label = "7d";
            percent = weeklyPercent.Value;
            resetsAt = subscriptionUsage.WeeklyResetsAt;
        }

        var resetPart = resetsAt is { } r ? $", {FormatRelative(r, now)}" : "";
        return $"Sub: {percent:F0}% ({label}){resetPart}";
    }

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

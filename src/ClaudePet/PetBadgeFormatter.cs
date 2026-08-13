using ClaudePet.Models;

namespace ClaudePet;

// Unlike TooltipFormatter (63-char NotifyIcon.Text budget forces picking one
// window), the pet-window badge has its own overlay and shows both windows
// on separate lines, matching Claude Desktop's usage settings panel.
public static class PetBadgeFormatter
{
    public static string? Format(SubscriptionUsageSnapshot? subscriptionUsage, DateTimeOffset? now = null)
    {
        if (subscriptionUsage is null)
            return null;

        var reference = now ?? DateTimeOffset.UtcNow;
        var lines = new List<string>();

        if (subscriptionUsage.FiveHourPercent is { } fiveHourPercent)
            lines.Add(FormatFiveHourLine(fiveHourPercent, subscriptionUsage.FiveHourResetsAt, reference));

        if (subscriptionUsage.WeeklyPercent is { } weeklyPercent)
            lines.Add(FormatWeeklyLine(weeklyPercent, subscriptionUsage.WeeklyResetsAt));

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string FormatFiveHourLine(double percent, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        var resetPart = resetsAt is { } r ? $" ({MinutesUntil(r, now)}m)" : "";
        return $"5h: {percent:F0}%{resetPart}";
    }

    private static string FormatWeeklyLine(double percent, DateTimeOffset? resetsAt)
    {
        var resetPart = resetsAt is { } r ? $" ({FormatDayAndHour(r)})" : "";
        return $"7d: {percent:F0}%{resetPart}";
    }

    private static int MinutesUntil(DateTimeOffset resetsAt, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Round((resetsAt - now).TotalMinutes));

    // "Sun 3PM": day abbreviation + hour in the user's local time, no
    // minutes - this is a reset window measured in days, so minute
    // precision would be false accuracy.
    private static string FormatDayAndHour(DateTimeOffset resetsAt)
    {
        var local = resetsAt.ToLocalTime();
        var hour12 = local.Hour % 12;
        if (hour12 == 0)
            hour12 = 12;
        var meridiem = local.Hour < 12 ? "AM" : "PM";
        var dayAbbreviation = local.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture);
        return $"{dayAbbreviation} {hour12}{meridiem}";
    }
}

using ClaudePet.Models;

namespace ClaudePet.Services;

public static class SubscriptionUsageParser
{
    // Header names are not confirmed against a live OAuth-authenticated
    // response (reconstructed from third-party reverse-engineering - see
    // the design spec's Open Assumptions). SubscriptionUsageReader (Task 4)
    // logs the full raw header set on first response so this can be
    // verified/corrected against a real logged-in session.
    private const string FiveHourUtilizationHeader = "anthropic-ratelimit-unified-5h-utilization";
    private const string FiveHourResetHeader = "anthropic-ratelimit-unified-5h-reset";
    private const string SevenDayUtilizationHeader = "anthropic-ratelimit-unified-7d-utilization";
    private const string SevenDayResetHeader = "anthropic-ratelimit-unified-7d-reset";

    public static SubscriptionUsageSnapshot? Parse(IReadOnlyDictionary<string, string> headers)
    {
        var fiveHour = ParseWindow(headers, FiveHourUtilizationHeader, FiveHourResetHeader);
        var sevenDay = ParseWindow(headers, SevenDayUtilizationHeader, SevenDayResetHeader);

        if (fiveHour is null && sevenDay is null)
            return null;

        return new SubscriptionUsageSnapshot(
            fiveHour?.Percent, fiveHour?.ResetsAt,
            sevenDay?.Percent, sevenDay?.ResetsAt);
    }

    private static (double Percent, DateTimeOffset? ResetsAt)? ParseWindow(
        IReadOnlyDictionary<string, string> headers, string utilizationHeader, string resetHeader)
    {
        if (!headers.TryGetValue(utilizationHeader, out var utilizationValue) ||
            !double.TryParse(
                utilizationValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var utilization))
        {
            return null;
        }

        var percent = Math.Clamp(utilization * 100.0, 0, 100);

        DateTimeOffset? resetsAt = null;
        if (headers.TryGetValue(resetHeader, out var resetValue) && long.TryParse(resetValue, out var resetSeconds))
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);

        return (percent, resetsAt);
    }
}

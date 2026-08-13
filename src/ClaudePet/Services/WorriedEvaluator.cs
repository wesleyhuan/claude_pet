using ClaudePet.Models;

namespace ClaudePet.Services;

public static class WorriedEvaluator
{
    private const double Threshold = 70.0;

    // Subscription usage (the real quota you can actually run out of) takes
    // priority when available; falls back to this session's context-window
    // usage when the opt-in subscription feature is off, not yet polled, or
    // currently failing.
    public static bool IsWorried(UsageSnapshot? usage, SubscriptionUsageSnapshot? subscriptionUsage)
    {
        if (subscriptionUsage is not null)
        {
            var fiveHour = subscriptionUsage.FiveHourPercent ?? 0.0;
            var weekly = subscriptionUsage.WeeklyPercent ?? 0.0;
            return Math.Max(fiveHour, weekly) >= Threshold;
        }

        return usage is not null && usage.Percent >= Threshold;
    }
}

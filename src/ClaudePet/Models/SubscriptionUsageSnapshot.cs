namespace ClaudePet.Models;

// Each window's Percent/ResetsAt pair is null together when that window's
// headers weren't present in the response (fields are independent because
// callers - the pet-window badge in particular - now display both windows
// side by side rather than picking whichever is more constrained).
public sealed record SubscriptionUsageSnapshot(
    double? FiveHourPercent,
    DateTimeOffset? FiveHourResetsAt,
    double? WeeklyPercent,
    DateTimeOffset? WeeklyResetsAt);

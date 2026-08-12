namespace ClaudePet.Models;

public sealed record SubscriptionUsageSnapshot(double Percent, string WindowLabel, DateTimeOffset? ResetsAt);

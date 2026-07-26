namespace ClaudePet.Models;

public sealed record RateLimitSnapshot(int? RemainingTokens, int? LimitTokens, double? Percent, DateTimeOffset? ResetsAt);

namespace ClaudePet.Models;

public sealed record UsageSnapshot(int ContextTokens, int ContextLimit, double Percent);

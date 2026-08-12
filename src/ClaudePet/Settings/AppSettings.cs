namespace ClaudePet.Settings;

public sealed record AppSettings
{
    // double? (not a -1 sentinel): a saved position is legitimately negative on
    // multi-monitor setups where a monitor sits left of/above the primary one, so
    // "-1 means unset" can't be distinguished from "-1 is a real coordinate". null
    // unambiguously means "no saved position yet".
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public bool RunAtStartup { get; init; }

    // Opt-in: polls Anthropic's undocumented anthropic-ratelimit-unified-*
    // headers via the user's Claude Code OAuth credential. See
    // docs/superpowers/specs/2026-08-09-subscription-usage-design.md.
    public bool ShowSubscriptionUsage { get; init; }
}

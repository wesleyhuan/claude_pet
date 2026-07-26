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
}

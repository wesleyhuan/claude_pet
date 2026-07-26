namespace ClaudePet.Settings;

public sealed record AppSettings
{
    public double WindowLeft { get; init; } = -1;
    public double WindowTop { get; init; } = -1;
    public bool RunAtStartup { get; init; }
}

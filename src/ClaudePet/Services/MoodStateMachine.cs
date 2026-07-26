using ClaudePet.Models;

namespace ClaudePet.Services;

public sealed class MoodStateMachine
{
    private const double EnterEatingAt = 40.0;
    private const double ExitEatingBelow = 35.0;
    private const double EnterFullAt = 75.0;
    private const double ExitFullBelow = 70.0;
    private const double EnterStressedAt = 90.0;
    private const double ExitStressedBelow = 85.0;

    public Mood Current { get; private set; } = Mood.NoSession;

    public Mood Update(UsageSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            Current = Mood.NoSession;
            return Current;
        }

        var percent = snapshot.Percent;

        Current = Current switch
        {
            Mood.NoSession or Mood.Happy => RisingMood(percent, floor: Mood.Happy),
            Mood.Eating => percent >= EnterStressedAt ? Mood.Stressed
                          : percent >= EnterFullAt ? Mood.Full
                          : percent < ExitEatingBelow ? Mood.Happy
                          : Mood.Eating,
            Mood.Full => percent >= EnterStressedAt ? Mood.Stressed
                        : percent < ExitFullBelow ? RisingMood(percent, floor: Mood.Happy)
                        : Mood.Full,
            Mood.Stressed => percent < ExitStressedBelow ? RisingMood(percent, floor: Mood.Happy)
                            : Mood.Stressed,
            _ => Mood.Happy
        };

        return Current;
    }

    private static Mood RisingMood(double percent, Mood floor) =>
        percent >= EnterStressedAt ? Mood.Stressed
        : percent >= EnterFullAt ? Mood.Full
        : percent >= EnterEatingAt ? Mood.Eating
        : floor;
}

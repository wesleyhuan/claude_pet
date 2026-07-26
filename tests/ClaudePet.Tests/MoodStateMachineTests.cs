using ClaudePet.Models;
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class MoodStateMachineTests
{
    private static UsageSnapshot AtPercent(double percent) => new(0, 200_000, percent);

    [Fact]
    public void Update_NullSnapshot_ReturnsNoSession()
    {
        var sm = new MoodStateMachine();

        var mood = sm.Update(null);

        Assert.Equal(Mood.NoSession, mood);
    }

    [Theory]
    [InlineData(0, Mood.Happy)]
    [InlineData(39, Mood.Happy)]
    [InlineData(40, Mood.Eating)]
    [InlineData(74, Mood.Eating)]
    [InlineData(75, Mood.Full)]
    [InlineData(89, Mood.Full)]
    [InlineData(90, Mood.Stressed)]
    [InlineData(100, Mood.Stressed)]
    public void Update_FromFreshState_MapsPercentToExpectedMood(double percent, Mood expected)
    {
        var sm = new MoodStateMachine();

        var mood = sm.Update(AtPercent(percent));

        Assert.Equal(expected, mood);
    }

    [Fact]
    public void Update_HoveringNearEnterThreshold_DoesNotFlipFlop()
    {
        var sm = new MoodStateMachine();
        sm.Update(AtPercent(38)); // Happy
        sm.Update(AtPercent(41)); // crosses into Eating (enter at 40)

        var mood = sm.Update(AtPercent(37)); // dips just below 40 again

        // Exit-Eating threshold is 35, so 37 should NOT flip back to Happy yet.
        Assert.Equal(Mood.Eating, mood);
    }

    [Fact]
    public void Update_DropsBelowExitThreshold_ReturnsToHappy()
    {
        var sm = new MoodStateMachine();
        sm.Update(AtPercent(50)); // Eating

        var mood = sm.Update(AtPercent(30)); // below exit-Eating threshold (35)

        Assert.Equal(Mood.Happy, mood);
    }

    [Fact]
    public void Update_RisingThenFalling_PassesThroughFullBeforeHappy()
    {
        var sm = new MoodStateMachine();
        sm.Update(AtPercent(95)); // Stressed

        var afterDrop = sm.Update(AtPercent(80)); // below exit-Stressed (85), above enter-Full (75)

        Assert.Equal(Mood.Full, afterDrop);
    }
}

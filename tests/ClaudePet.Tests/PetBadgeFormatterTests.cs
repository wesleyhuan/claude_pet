using ClaudePet.Models;

namespace ClaudePet.Tests;

public class PetBadgeFormatterTests
{
    // FormatDayAndHour converts via .ToLocalTime(), so a UTC timestamp's
    // rendered day/hour depends on the test machine's time zone. Building
    // from the local offset instead pins the wall-clock day/hour these
    // tests assert on, independent of where they run.
    private static DateTimeOffset LocalTime(int year, int month, int day, int hour)
    {
        var naive = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(naive, TimeZoneInfo.Local.GetUtcOffset(naive));
    }

    [Fact]
    public void Format_NullSnapshot_ReturnsNull()
    {
        var result = PetBadgeFormatter.Format(null);

        Assert.Null(result);
    }

    [Fact]
    public void Format_BothWindowsPresent_ProducesTwoLines()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new SubscriptionUsageSnapshot(
            34.0, now.AddMinutes(142),
            17.0, LocalTime(2026, 8, 16, 15));

        var result = PetBadgeFormatter.Format(snapshot, now);

        Assert.Equal("5h: 34% (142m)\n7d: 17% (Sun 3PM)", result);
    }

    [Fact]
    public void Format_OnlyFiveHourPresent_OmitsWeeklyLine()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SubscriptionUsageSnapshot(34.0, now.AddMinutes(10), null, null);

        var result = PetBadgeFormatter.Format(snapshot, now);

        Assert.Equal("5h: 34% (10m)", result);
    }

    [Fact]
    public void Format_OnlyWeeklyPresent_OmitsFiveHourLine()
    {
        var snapshot = new SubscriptionUsageSnapshot(
            null, null, 17.0, LocalTime(2026, 8, 16, 15));

        var result = PetBadgeFormatter.Format(snapshot);

        Assert.Equal("7d: 17% (Sun 3PM)", result);
    }

    [Fact]
    public void Format_ResetsAtMissing_OmitsParenthetical()
    {
        var snapshot = new SubscriptionUsageSnapshot(34.0, null, 17.0, null);

        var result = PetBadgeFormatter.Format(snapshot);

        Assert.Equal("5h: 34%\n7d: 17%", result);
    }

    [Fact]
    public void Format_ResetInPast_ClampsMinutesToZero()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new SubscriptionUsageSnapshot(34.0, now.AddMinutes(-5), null, null);

        var result = PetBadgeFormatter.Format(snapshot, now);

        Assert.Equal("5h: 34% (0m)", result);
    }

    [Fact]
    public void Format_WeeklyResetAtNoon_UsesTwelvePmNotZeroPm()
    {
        var snapshot = new SubscriptionUsageSnapshot(
            null, null, 17.0, LocalTime(2026, 8, 16, 12));

        var result = PetBadgeFormatter.Format(snapshot);

        Assert.Equal("7d: 17% (Sun 12PM)", result);
    }

    [Fact]
    public void Format_WeeklyResetAtMidnight_UsesTwelveAmNotZeroAm()
    {
        var snapshot = new SubscriptionUsageSnapshot(
            null, null, 17.0, LocalTime(2026, 8, 16, 0));

        var result = PetBadgeFormatter.Format(snapshot);

        Assert.Equal("7d: 17% (Sun 12AM)", result);
    }
}

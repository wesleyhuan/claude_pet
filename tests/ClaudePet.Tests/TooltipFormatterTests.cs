using ClaudePet.Models;
using ClaudePet.Tray;

namespace ClaudePet.Tests;

public class TooltipFormatterTests
{
    [Fact]
    public void Format_NullUsageNullRateLimit_ReturnsNoActiveSessionLine()
    {
        var result = TooltipFormatter.Format(null, null);

        Assert.Equal("Claude Pet: no active session", result);
    }

    [Fact]
    public void Format_UsageOnly_MatchesOriginalSingleLineFormat()
    {
        var usage = new UsageSnapshot(443155, 1000000, 44.3);

        var result = TooltipFormatter.Format(usage, null);

        Assert.Equal("Claude Pet: 44% (443,155/1,000,000)", result);
    }

    [Fact]
    public void Format_UsageAndRateLimitWithReset_ProducesTwoLines()
    {
        var now = DateTimeOffset.UtcNow;
        var usage = new UsageSnapshot(443155, 1000000, 44.3);
        var rateLimit = new RateLimitSnapshot(880000, 1000000, 12.0, now.AddHours(3).AddMinutes(20));

        var result = TooltipFormatter.Format(usage, rateLimit, now);

        Assert.Equal("Claude Pet: 44% (443,155/1,000,000)\nRate limit: 12% used, 3h", result);
        Assert.True(result.Length <= TooltipFormatter.MaxLength);
    }

    [Fact]
    public void Format_RateLimitWithoutResetsAt_OmitsResetClause()
    {
        var rateLimit = new RateLimitSnapshot(500, 1000, 50.0, null);

        var result = TooltipFormatter.Format(null, rateLimit);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used", result);
    }

    [Fact]
    public void Format_RateLimitWithNullPercent_OmitsSecondLineEntirely()
    {
        var rateLimit = new RateLimitSnapshot(null, null, null, null);

        var result = TooltipFormatter.Format(null, rateLimit);

        Assert.Equal("Claude Pet: no active session", result);
        Assert.DoesNotContain("Rate limit", result);
    }

    [Fact]
    public void Format_CombinedTextExceedsLimit_TruncatesRateLimitLineFirst()
    {
        // With the compact reset format, a normal-scale usage line (e.g.
        // 999,999/1,000,000) leaves enough budget for "Rate limit: NN%
        // used, Xh" to fit without truncation. Use int.MaxValue-scale
        // token counts (like the extreme-values test) to force a long
        // enough line 1 that line 2 must still be truncated.
        var now = DateTimeOffset.UtcNow;
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var rateLimit = new RateLimitSnapshot(1, 1000000, 99.9999, now.AddHours(23).AddMinutes(59));

        var result = TooltipFormatter.Format(usage, rateLimit, now);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
        Assert.StartsWith("Claude Pet: 100% (2,147,483,647/2,147,483,647)\n", result);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Format_ResultNeverExceedsMaxLength_EvenWithExtremeValues()
    {
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var rateLimit = new RateLimitSnapshot(0, int.MaxValue, 100.0, DateTimeOffset.UtcNow.AddDays(400));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
    }

    [Fact]
    public void Format_ResetsAtInPast_ShowsSoon()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddMinutes(-5));

        var result = TooltipFormatter.Format(null, rateLimit, now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, soon", result);
    }

    [Fact]
    public void Format_ResetsAtUnderOneHour_ShowsMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddMinutes(45));

        var result = TooltipFormatter.Format(null, rateLimit, now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 45m", result);
    }

    [Fact]
    public void Format_ResetsAtUnderOneMinute_FloorsToOneMinute()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddSeconds(20));

        var result = TooltipFormatter.Format(null, rateLimit, now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 1m", result);
    }

    [Fact]
    public void Format_ResetsAtWithinADay_ShowsHours()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddHours(5).AddMinutes(30));

        var result = TooltipFormatter.Format(null, rateLimit, now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 5h", result);
    }

    [Fact]
    public void Format_ResetsAtBeyondADay_ShowsDays()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddDays(2).AddHours(3));

        var result = TooltipFormatter.Format(null, rateLimit, now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 2d", result);
    }
}

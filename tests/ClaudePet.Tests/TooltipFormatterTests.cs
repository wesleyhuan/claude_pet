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
        var usage = new UsageSnapshot(443155, 1000000, 44.3);
        var rateLimit = new RateLimitSnapshot(880000, 1000000, 12.0, DateTimeOffset.UtcNow.AddHours(3).AddMinutes(20));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.StartsWith("Claude Pet: 44% (443,155/1,000,000)\nRate limit: 12% used, reset", result);
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
        var usage = new UsageSnapshot(999999, 1000000, 100.0);
        var rateLimit = new RateLimitSnapshot(1, 1000000, 99.9999, DateTimeOffset.UtcNow.AddHours(23).AddMinutes(59));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
        Assert.StartsWith("Claude Pet: 100% (999,999/1,000,000)\n", result);
    }

    [Fact]
    public void Format_ResultNeverExceedsMaxLength_EvenWithExtremeValues()
    {
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var rateLimit = new RateLimitSnapshot(0, int.MaxValue, 100.0, DateTimeOffset.UtcNow.AddDays(400));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
    }
}

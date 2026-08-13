using ClaudePet.Models;
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class WorriedEvaluatorTests
{
    [Fact]
    public void IsWorried_BothNull_ReturnsFalse()
    {
        Assert.False(WorriedEvaluator.IsWorried(null, null));
    }

    [Theory]
    [InlineData(69.9, false)]
    [InlineData(70.0, true)]
    [InlineData(85.0, true)]
    public void IsWorried_NoSubscriptionData_UsesSessionUsageThreshold(double percent, bool expected)
    {
        var usage = new UsageSnapshot(0, 200_000, percent);

        Assert.Equal(expected, WorriedEvaluator.IsWorried(usage, null));
    }

    [Fact]
    public void IsWorried_SubscriptionPresent_IgnoresSessionUsage()
    {
        var usage = new UsageSnapshot(0, 200_000, 99.0);
        var subscriptionUsage = new SubscriptionUsageSnapshot(10.0, null, 20.0, null);

        Assert.False(WorriedEvaluator.IsWorried(usage, subscriptionUsage));
    }

    [Fact]
    public void IsWorried_SubscriptionFiveHourAboveThreshold_ReturnsTrue()
    {
        var subscriptionUsage = new SubscriptionUsageSnapshot(70.0, null, 10.0, null);

        Assert.True(WorriedEvaluator.IsWorried(null, subscriptionUsage));
    }

    [Fact]
    public void IsWorried_SubscriptionWeeklyAboveThreshold_ReturnsTrue()
    {
        var subscriptionUsage = new SubscriptionUsageSnapshot(10.0, null, 70.0, null);

        Assert.True(WorriedEvaluator.IsWorried(null, subscriptionUsage));
    }

    [Fact]
    public void IsWorried_SubscriptionBothBelowThreshold_ReturnsFalse()
    {
        var subscriptionUsage = new SubscriptionUsageSnapshot(69.0, null, 69.0, null);

        Assert.False(WorriedEvaluator.IsWorried(null, subscriptionUsage));
    }

    [Fact]
    public void IsWorried_SubscriptionWindowsPartiallyMissing_TreatsMissingAsZero()
    {
        var subscriptionUsage = new SubscriptionUsageSnapshot(null, null, 40.0, null);

        Assert.False(WorriedEvaluator.IsWorried(null, subscriptionUsage));
    }
}

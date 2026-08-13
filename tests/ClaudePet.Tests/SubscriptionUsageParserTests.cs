using ClaudePet.Services;

namespace ClaudePet.Tests;

public class SubscriptionUsageParserTests
{
    private static Dictionary<string, string> Headers(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Parse_NoHeadersPresent_ReturnsNull()
    {
        var result = SubscriptionUsageParser.Parse(Headers());

        Assert.Null(result);
    }

    [Fact]
    public void Parse_OnlyFiveHourPresent_LeavesWeeklyNull()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.33"),
            ("anthropic-ratelimit-unified-5h-reset", "1774933200"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(33.0, result!.FiveHourPercent!.Value, precision: 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1774933200), result.FiveHourResetsAt);
        Assert.Null(result.WeeklyPercent);
        Assert.Null(result.WeeklyResetsAt);
    }

    [Fact]
    public void Parse_OnlySevenDayPresent_LeavesFiveHourNull()
    {
        var headers = Headers(("anthropic-ratelimit-unified-7d-utilization", "0.13"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(13.0, result!.WeeklyPercent!.Value, precision: 3);
        Assert.Null(result.WeeklyResetsAt);
        Assert.Null(result.FiveHourPercent);
    }

    [Fact]
    public void Parse_BothPresent_PopulatesBothWindowsIndependently()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.33"),
            ("anthropic-ratelimit-unified-5h-reset", "1774933200"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.53"),
            ("anthropic-ratelimit-unified-7d-reset", "1775019600"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(33.0, result!.FiveHourPercent!.Value, precision: 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1774933200), result.FiveHourResetsAt);
        Assert.Equal(53.0, result.WeeklyPercent!.Value, precision: 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1775019600), result.WeeklyResetsAt);
    }

    [Fact]
    public void Parse_NonNumericUtilization_TreatedAsWindowAbsent()
    {
        var headers = Headers(("anthropic-ratelimit-unified-5h-utilization", "not-a-number"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_UtilizationAboveOne_ClampsToOneHundredPercent()
    {
        // Defensive clamp: tolerate an unexpected already-percent-scale value
        // rather than producing an invalid (e.g. 8200%) display.
        var headers = Headers(("anthropic-ratelimit-unified-5h-utilization", "82"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(100.0, result!.FiveHourPercent!.Value);
    }

    [Fact]
    public void Parse_NonNumericReset_LeavesResetsAtNull()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.10"),
            ("anthropic-ratelimit-unified-5h-reset", "not-a-timestamp"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Null(result!.FiveHourResetsAt);
    }

    [Fact]
    public void Parse_MixedCaseHeaderKeys_StillResolves()
    {
        var headers = Headers(("ANTHROPIC-RATELIMIT-UNIFIED-5H-UTILIZATION", "0.45"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(45.0, result!.FiveHourPercent!.Value, precision: 3);
    }
}

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
    public void Parse_OnlyFiveHourPresent_UsesFiveHour()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.33"),
            ("anthropic-ratelimit-unified-5h-reset", "1774933200"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(33.0, result!.Percent, precision: 3);
        Assert.Equal("5h", result.WindowLabel);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1774933200), result.ResetsAt);
    }

    [Fact]
    public void Parse_OnlySevenDayPresent_UsesSevenDay()
    {
        var headers = Headers(("anthropic-ratelimit-unified-7d-utilization", "0.13"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(13.0, result!.Percent, precision: 3);
        Assert.Equal("7d", result.WindowLabel);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void Parse_BothPresentSevenDayHigher_PicksSevenDay()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.33"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.53"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("7d", result!.WindowLabel);
        Assert.Equal(53.0, result.Percent, precision: 3);
    }

    [Fact]
    public void Parse_BothPresentFiveHourHigher_PicksFiveHour()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.82"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.20"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("5h", result!.WindowLabel);
        Assert.Equal(82.0, result.Percent, precision: 3);
    }

    [Fact]
    public void Parse_ExactTie_PrefersFiveHour()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.50"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.50"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("5h", result!.WindowLabel);
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
        // rather than producing an invalid (e.g. 8200%) tooltip line.
        var headers = Headers(("anthropic-ratelimit-unified-5h-utilization", "82"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(100.0, result!.Percent);
    }

    [Fact]
    public void Parse_NonNumericReset_LeavesResetsAtNull()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.10"),
            ("anthropic-ratelimit-unified-5h-reset", "not-a-timestamp"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Null(result!.ResetsAt);
    }

    [Fact]
    public void Parse_MixedCaseHeaderKeys_StillResolves()
    {
        var headers = Headers(("ANTHROPIC-RATELIMIT-UNIFIED-5H-UTILIZATION", "0.45"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(45.0, result!.Percent, precision: 3);
    }
}

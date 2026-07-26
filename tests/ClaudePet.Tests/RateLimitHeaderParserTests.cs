using ClaudePet.Services;

namespace ClaudePet.Tests;

public class RateLimitHeaderParserTests
{
    private static Dictionary<string, string> Headers(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Parse_AllHeadersPresentWithIsoDateReset_ProducesFullSnapshot()
    {
        var resetTime = DateTimeOffset.UtcNow.AddHours(2);
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "880000"),
            ("anthropic-ratelimit-tokens-limit", "1000000"),
            ("anthropic-ratelimit-tokens-reset", resetTime.ToString("O")));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(880000, result.RemainingTokens);
        Assert.Equal(1000000, result.LimitTokens);
        Assert.NotNull(result.Percent);
        Assert.Equal(12.0, result.Percent!.Value, precision: 3);
        Assert.NotNull(result.ResetsAt);
        Assert.Equal(resetTime, result.ResetsAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Parse_ResetAsIntegerSecondsFromNow_ResolvesToApproximateFutureTime()
    {
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "500"),
            ("anthropic-ratelimit-tokens-limit", "1000"),
            ("anthropic-ratelimit-tokens-reset", "3600"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.NotNull(result.ResetsAt);
        var expected = DateTimeOffset.UtcNow.AddSeconds(3600);
        Assert.Equal(expected, result.ResetsAt!.Value, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_NoHeadersPresent_ProducesAllNullSnapshot()
    {
        var headers = Headers();

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Null(result.RemainingTokens);
        Assert.Null(result.LimitTokens);
        Assert.Null(result.Percent);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void Parse_OnlyRemainingAndLimitPresent_ComputesPercentWithNullResetsAt()
    {
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "250"),
            ("anthropic-ratelimit-tokens-limit", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(250, result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
        Assert.Equal(75.0, result.Percent);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void Parse_NonNumericRemainingValue_TreatedAsMissing()
    {
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "not-a-number"),
            ("anthropic-ratelimit-tokens-limit", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Null(result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
        Assert.Null(result.Percent);
    }

    [Fact]
    public void Parse_FallsBackToInputTokensVariant_WhenPrimaryHeaderAbsent()
    {
        var headers = Headers(
            ("anthropic-ratelimit-input-tokens-remaining", "900"),
            ("anthropic-ratelimit-input-tokens-limit", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(900, result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
        Assert.Equal(10.0, result.Percent);
    }

    [Fact]
    public void Parse_MixedCaseHeaderKeys_StillResolves()
    {
        var headers = Headers(
            ("Anthropic-RateLimit-Tokens-Remaining", "700"),
            ("ANTHROPIC-RATELIMIT-TOKENS-LIMIT", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(700, result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
    }
}

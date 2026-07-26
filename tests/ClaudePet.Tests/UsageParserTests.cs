using ClaudePet.Services;

namespace ClaudePet.Tests;

public class UsageParserTests
{
    private const string ValidLine =
        """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":2,"cache_creation_input_tokens":19271,"cache_read_input_tokens":26677,"output_tokens":874}}}""";

    [Fact]
    public void TryParseLine_ValidAssistantLine_ComputesContextTokens()
    {
        var result = UsageParser.TryParseLine(ValidLine);

        Assert.NotNull(result);
        Assert.Equal(2 + 19271 + 26677, result!.ContextTokens);
    }

    [Fact]
    public void TryParseLine_ValidAssistantLine_ComputesPercentOfKnownModelLimit()
    {
        var result = UsageParser.TryParseLine(ValidLine);

        Assert.NotNull(result);
        Assert.Equal(1_000_000, result!.ContextLimit);
        Assert.Equal((2 + 19271 + 26677) / 1_000_000.0 * 100.0, result.Percent, precision: 6);
    }

    [Fact]
    public void TryParseLine_UnrecognizedModel_FallsBackToDefaultLimitAndReportsIt()
    {
        const string line = """{"type":"assistant","message":{"model":"claude-future-9","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":1}}}""";
        string? reportedModel = null;

        var result = UsageParser.TryParseLine(line, model => reportedModel = model);

        Assert.NotNull(result);
        Assert.Equal(1_000_000, result!.ContextLimit);
        Assert.Equal("claude-future-9", reportedModel);
    }

    [Fact]
    public void TryParseLine_ClaudeOpus5_UsesOneMillionLimit()
    {
        const string line = """{"type":"assistant","message":{"model":"claude-opus-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":1}}}""";
        string? reportedModel = null;

        var result = UsageParser.TryParseLine(line, model => reportedModel = model);

        Assert.NotNull(result);
        Assert.Equal(1_000_000, result!.ContextLimit);
        Assert.Null(reportedModel);
    }

    [Fact]
    public void TryParseLine_ClaudeHaiku_FallsBackToDefaultLimit()
    {
        // No haiku entry exists in ContextLimits (no real haiku session data has
        // been observed to validate a specific limit against), so it must fall
        // through to the same safe default as any other unrecognized model.
        const string line = """{"type":"assistant","message":{"model":"claude-haiku-4-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":1}}}""";
        string? reportedModel = null;

        var result = UsageParser.TryParseLine(line, model => reportedModel = model);

        Assert.NotNull(result);
        Assert.Equal(1_000_000, result!.ContextLimit);
        Assert.Equal("claude-haiku-4-5", reportedModel);
    }

    [Theory]
    [InlineData("claude-sonnet-5", 379_436)]
    [InlineData("claude-opus-4-8", 562_832)]
    public void TryParseLine_RealisticLargeSessionTotals_AreNotSaturatedAt100Percent(string model, int totalContextTokens)
    {
        // Real observed context totals from actual session logs on this machine —
        // both previously exceeded the old 200,000 hardcoded limit and saturated
        // Math.Clamp at 100%, permanently pinning the pet to "Stressed".
        string line =
            "{\"type\":\"assistant\",\"message\":{\"model\":\"" + model +
            "\",\"usage\":{\"input_tokens\":0,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":" +
            totalContextTokens + ",\"output_tokens\":1}}}";

        var result = UsageParser.TryParseLine(line);

        Assert.NotNull(result);
        Assert.Equal(totalContextTokens, result!.ContextTokens);
        Assert.Equal(1_000_000, result.ContextLimit);
        Assert.True(result.Percent < 100.0, $"Expected percent well below 100, got {result.Percent}");
        Assert.True(result.Percent < 60.0, $"Expected percent well below saturation, got {result.Percent}");
    }

    [Fact]
    public void TryParseLine_RealCapturedSessionLogExcerpt_ExtractsTopLevelTokensIgnoringNestedFields()
    {
        // Verified against actual Claude Code output on this machine. Includes a
        // nested cache_creation object and an iterations array with similarly-named
        // but irrelevant nested token fields, which must NOT be picked up instead of
        // the top-level usage.* fields.
        const string line =
            """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":2,"cache_creation_input_tokens":19271,"cache_read_input_tokens":26677,"output_tokens":874,"server_tool_use":{"web_search_requests":0,"web_fetch_requests":0},"service_tier":"standard","cache_creation":{"ephemeral_1h_input_tokens":19271,"ephemeral_5m_input_tokens":0},"inference_geo":"not_available","iterations":[{"input_tokens":2,"output_tokens":874,"cache_read_input_tokens":26677,"cache_creation_input_tokens":19271,"cache_creation":{"ephemeral_5m_input_tokens":0,"ephemeral_1h_input_tokens":19271},"type":"message"}],"speed":"standard"}}}""";

        var result = UsageParser.TryParseLine(line);

        Assert.NotNull(result);
        Assert.Equal(2 + 19271 + 26677, result!.ContextTokens);
        Assert.Equal(45950, result.ContextTokens);
    }

    [Fact]
    public void TryParseLine_SyntheticModelWithAllZeroUsage_ReturnsNull()
    {
        const string line =
            """{"type":"assistant","isSidechain":false,"message":{"model":"<synthetic>","usage":{"input_tokens":0,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}}""";

        var result = UsageParser.TryParseLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseLatest_SyntheticLineAfterRealLine_DoesNotOverrideLatestRealSnapshot()
    {
        const string real = """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":5000,"output_tokens":1}}}""";
        const string synthetic = """{"type":"assistant","isSidechain":false,"message":{"model":"<synthetic>","usage":{"input_tokens":0,"output_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}}""";

        var result = UsageParser.ParseLatest(new[] { real, synthetic });

        Assert.NotNull(result);
        Assert.Equal(5001, result!.ContextTokens);
    }

    [Fact]
    public void TryParseLine_MissingUsageObject_ReturnsNull()
    {
        const string line = """{"type":"user","message":{"role":"user","content":"hi"}}""";

        var result = UsageParser.TryParseLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseLine_MalformedJson_ReturnsNull()
    {
        const string line = """{"type":"assistant","message":{"usage":{"input_tokens":""";

        var result = UsageParser.TryParseLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseLine_BlankLine_ReturnsNull()
    {
        Assert.Null(UsageParser.TryParseLine(""));
        Assert.Null(UsageParser.TryParseLine("   "));
    }

    [Fact]
    public void ParseLatest_MultipleLines_ReturnsLastValidOne()
    {
        const string first = """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":1000,"output_tokens":1}}}""";
        const string second = """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":5000,"output_tokens":1}}}""";

        var result = UsageParser.ParseLatest(new[] { first, "not json", second });

        Assert.NotNull(result);
        Assert.Equal(5001, result!.ContextTokens);
    }

    [Fact]
    public void ParseLatest_NoValidLines_ReturnsNull()
    {
        var result = UsageParser.ParseLatest(new[] { "", "not json", """{"type":"user"}""" });

        Assert.Null(result);
    }

    [Fact]
    public void TryParseLine_UsageFieldIsNotAnObject_ReturnsNull()
    {
        const string line = """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":"oops"}}""";

        var result = UsageParser.TryParseLine(line);

        Assert.Null(result);
    }
}

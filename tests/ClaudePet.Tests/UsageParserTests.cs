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
        Assert.Equal(200_000, result!.ContextLimit);
        Assert.Equal((2 + 19271 + 26677) / 200_000.0 * 100.0, result.Percent, precision: 6);
    }

    [Fact]
    public void TryParseLine_UnrecognizedModel_FallsBackToDefaultLimitAndReportsIt()
    {
        const string line = """{"type":"assistant","message":{"model":"claude-future-9","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":1}}}""";
        string? reportedModel = null;

        var result = UsageParser.TryParseLine(line, model => reportedModel = model);

        Assert.NotNull(result);
        Assert.Equal(200_000, result!.ContextLimit);
        Assert.Equal("claude-future-9", reportedModel);
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

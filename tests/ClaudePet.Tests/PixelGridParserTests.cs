using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PixelGridParserTests
{
    private static string ValidGridJson() => """
    {
      "palette": { "R": "FF4CAF50", ".": "00000000" },
      "pixels": [
        "................",
        "................",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "................",
        "................"
      ]
    }
    """;

    [Fact]
    public void Parse_ValidGrid_ProducesExpectedPixels()
    {
        var frame = PixelGridParser.Parse(ValidGridJson());

        Assert.NotNull(frame);
        Assert.Equal(16, frame!.Width);
        Assert.Equal(16, frame.Height);
        Assert.Equal(0xFF4CAF50u, frame[2, 2]);
        Assert.Equal(0u, frame[0, 0]);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNullAndReportsError()
    {
        string? error = null;

        var frame = PixelGridParser.Parse("{ not valid json ", err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_MissingPalette_ReturnsNull()
    {
        var frame = PixelGridParser.Parse("""{ "pixels": [] }""");

        Assert.Null(frame);
    }

    [Fact]
    public void Parse_WrongRowCount_ReturnsNullAndReportsError()
    {
        var json = """
        {
          "palette": { ".": "00000000" },
          "pixels": [ "................" ]
        }
        """;
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.Contains("16", error);
    }

    [Fact]
    public void Parse_WrongColumnCount_ReturnsNullAndReportsError()
    {
        var rows = Enumerable.Repeat("\"...\"", 16);
        var json = $$"""{ "palette": { ".": "00000000" }, "pixels": [ {{string.Join(",", rows)}} ] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_UnresolvedPaletteCharacter_ReturnsNullAndReportsError()
    {
        var rows = Enumerable.Repeat("\"................\"", 15).Append("\"X...............\"");
        var json = $$"""{ "palette": { ".": "00000000" }, "pixels": [ {{string.Join(",", rows)}} ] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.Contains("X", error);
    }

    [Fact]
    public void Parse_InvalidHexInPalette_ReturnsNullAndReportsError()
    {
        var rows = Enumerable.Repeat("\"................\"", 16);
        var json = $$"""{ "palette": { ".": "ZZZZZZZZ" }, "pixels": [ {{string.Join(",", rows)}} ] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_PartialAlphaPaletteValue_PreservesAlphaByte()
    {
        var rows = Enumerable.Repeat("\"................\"", 15).Append("\"H...............\"");
        var json = $$"""{ "palette": { ".": "00000000", "H": "80112233" }, "pixels": [ {{string.Join(",", rows)}} ] }""";

        var frame = PixelGridParser.Parse(json);

        Assert.NotNull(frame);
        Assert.Equal(0x80112233u, frame![0, 15]);
    }

    [Fact]
    public void Parse_MultiCharacterPaletteKey_ReturnsNullAndReportsError()
    {
        var json = """{ "palette": { "AB": "00000000" }, "pixels": [] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }
}

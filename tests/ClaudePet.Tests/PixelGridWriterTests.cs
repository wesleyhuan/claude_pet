using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PixelGridWriterTests
{
    [Fact]
    public void Write_ThenParse_RoundTripsPixelsExactly()
    {
        var pixels = new uint[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i % 3 == 0 ? 0xFF4CAF50u : i % 3 == 1 ? 0x00000000u : 0x80112233u;
        var original = new PixelFrame(16, 16, pixels);

        var json = PixelGridWriter.Write(original);
        var parsed = PixelGridParser.Parse(json);

        Assert.NotNull(parsed);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(original[x, y], parsed![x, y]);
    }

    [Fact]
    public void Write_FullyTransparentFrame_RoundTripsToAllTransparent()
    {
        var pixels = new uint[16 * 16];
        var original = new PixelFrame(16, 16, pixels);

        var json = PixelGridWriter.Write(original);
        var parsed = PixelGridParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(0u, parsed![0, 0]);
        Assert.Equal(0u, parsed[15, 15]);
    }

    [Fact]
    public void Write_ProducesValidJsonParseableByPixelGridParser()
    {
        var pixels = new uint[16 * 16];
        Array.Fill(pixels, 0xFFAABBCCu);
        var original = new PixelFrame(16, 16, pixels);

        var json = PixelGridWriter.Write(original);

        string? error = null;
        var parsed = PixelGridParser.Parse(json, err => error = err);
        Assert.NotNull(parsed);
        Assert.Null(error);
    }
}

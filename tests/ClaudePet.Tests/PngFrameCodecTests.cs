using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PngFrameCodecTests
{
    private static PixelFrame SampleFrame()
    {
        var pixels = new uint[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i % 4 == 0 ? 0xFF4CAF50u : i % 4 == 1 ? 0x00000000u : i % 4 == 2 ? 0xFF212121u : 0x80AABBCCu;
        return new PixelFrame(16, 16, pixels);
    }

    [Fact]
    public void Encode_ThenDecode_RoundTripsPixelsExactly()
    {
        var original = SampleFrame();

        var bytes = PngFrameCodec.Encode(original);
        var decoded = PngFrameCodec.Decode(bytes);

        Assert.NotNull(decoded);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(original[x, y], decoded![x, y]);
    }

    [Fact]
    public void Decode_NotAPngFile_ReturnsNullAndReportsError()
    {
        string? error = null;

        var result = PngFrameCodec.Decode(new byte[] { 1, 2, 3, 4 }, err => error = err);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Decode_WrongDimensions_ReturnsNullAndReportsError()
    {
        var pixels = new uint[8 * 8];
        var wrongSizeFrame = new PixelFrame(8, 8, pixels);
        var bytes = PngFrameCodec.Encode(wrongSizeFrame);
        string? error = null;

        var result = PngFrameCodec.Decode(bytes, err => error = err);

        Assert.Null(result);
        Assert.Contains("16", error);
    }
}

using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PixelCompositorTests
{
    private static PixelFrame SolidFrame(int width, int height, uint color)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, color);
        return new PixelFrame(width, height, pixels);
    }

    [Fact]
    public void CompositeOver_OverlayFullyTransparent_ReturnsBaseUnchanged()
    {
        var @base = SolidFrame(2, 1, 0xFF112233);
        var overlay = SolidFrame(2, 1, 0x00000000);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0xFF112233u, result[0, 0]);
        Assert.Equal(0xFF112233u, result[1, 0]);
    }

    [Fact]
    public void CompositeOver_OverlayFullyOpaque_ReplacesBase()
    {
        var @base = SolidFrame(1, 1, 0xFF112233);
        var overlay = SolidFrame(1, 1, 0xFFAABBCC);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0xFFAABBCCu, result[0, 0]);
    }

    [Fact]
    public void CompositeOver_BaseFullyTransparentOverlayFullyOpaque_ReplacesBase()
    {
        var @base = SolidFrame(1, 1, 0x00000000);
        var overlay = SolidFrame(1, 1, 0xFFAABBCC);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0xFFAABBCCu, result[0, 0]);
    }

    [Fact]
    public void CompositeOver_PartialAlphaOverlay_BlendsProportionally()
    {
        // 50% alpha white over opaque black -> ~mid-grey, fully opaque.
        var @base = SolidFrame(1, 1, 0xFF000000);
        var overlay = SolidFrame(1, 1, 0x80FFFFFF);

        var result = PixelCompositor.CompositeOver(@base, overlay)[0, 0];

        var alpha = result >> 24;
        var red = (result >> 16) & 0xFF;
        Assert.Equal(0xFFu, alpha);
        Assert.InRange((int)red, 120, 135);
    }

    [Fact]
    public void CompositeOver_BothFullyTransparent_StaysFullyTransparent()
    {
        var @base = SolidFrame(1, 1, 0x00000000);
        var overlay = SolidFrame(1, 1, 0x00000000);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0u, result[0, 0] >> 24);
    }

    [Fact]
    public void CompositeOver_PreservesFrameDimensions()
    {
        var @base = SolidFrame(3, 2, 0xFF000000);
        var overlay = SolidFrame(3, 2, 0x00000000);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(3, result.Width);
        Assert.Equal(2, result.Height);
    }
}

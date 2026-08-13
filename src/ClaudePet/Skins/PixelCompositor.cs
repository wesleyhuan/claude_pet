using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class PixelCompositor
{
    // Straight (non-premultiplied) alpha "src over dst" - the app's PixelFrame
    // pixels are always straight alpha (a fully transparent pixel is exactly
    // 0x00000000; an opaque one always carries its full, unscaled RGB - see
    // PixelFrame/PixelArtGenerator).
    public static PixelFrame CompositeOver(PixelFrame @base, PixelFrame overlay)
    {
        var pixels = new uint[@base.Width * @base.Height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = CompositePixel(@base.Pixels[i], overlay.Pixels[i]);

        return new PixelFrame(@base.Width, @base.Height, pixels);
    }

    private static uint CompositePixel(uint dst, uint src)
    {
        var srcA = (byte)(src >> 24);
        if (srcA == 0)
            return dst;
        if (srcA == 255)
            return src;

        var srcR = (byte)(src >> 16);
        var srcG = (byte)(src >> 8);
        var srcB = (byte)src;
        var dstA = (byte)(dst >> 24);
        var dstR = (byte)(dst >> 16);
        var dstG = (byte)(dst >> 8);
        var dstB = (byte)dst;

        int outA = srcA + dstA * (255 - srcA) / 255;
        if (outA == 0)
            return 0;

        byte Blend(byte s, byte d) => (byte)((s * srcA + d * dstA * (255 - srcA) / 255) / outA);

        var outR = Blend(srcR, dstR);
        var outG = Blend(srcG, dstG);
        var outB = Blend(srcB, dstB);

        return (uint)(outA << 24 | outR << 16 | outG << 8 | outB);
    }
}

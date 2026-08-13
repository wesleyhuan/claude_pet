using ClaudePet.Models;

namespace ClaudePet.Rendering;

public static class PixelArtGenerator
{
    private const int Size = 16;
    private const uint Transparent = 0x00000000;
    private const uint EyeColor = 0xFF212121;
    // Overlay accents - independent of mood/body color, so both can render
    // at once (e.g. worried while the body is still Full-colored).
    private const uint WorkingColor = 0xFF29B6F6;
    private const uint WorriedColor = 0xFF81D4FA;

    public static IReadOnlyList<PixelFrame> GenerateFrames(Mood mood, bool isWorking = false, bool isWorried = false)
    {
        var bodyColor = BodyColor(mood);
        return new[]
        {
            GenerateFrame(bodyColor, mood, squish: false, isWorking, isWorried),
            GenerateFrame(bodyColor, mood, squish: true, isWorking, isWorried)
        };
    }

    // Standalone overlay frames (transparent everywhere except the accent
    // pixels) - used by GenerateFrame below, and reused directly by
    // ExampleSkinGenerator to seed the starter skin's overlay art without
    // duplicating this coordinate math.
    public static PixelFrame GenerateWorkingOverlayFrame(bool squish)
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, Transparent);

        int top = squish ? 3 : 2;
        int right = Size - 3;
        int sparkX = squish ? right - 2 : right - 1;
        pixels[top * Size + sparkX] = WorkingColor;

        return new PixelFrame(Size, Size, pixels);
    }

    public static PixelFrame GenerateWorriedOverlayFrame(bool squish)
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, Transparent);

        int top = squish ? 3 : 2;
        int left = 2;
        pixels[top * Size + left] = WorriedColor;
        pixels[(top + 1) * Size + left] = WorriedColor;
        pixels[(top + 1) * Size + (left + 1)] = WorriedColor;

        return new PixelFrame(Size, Size, pixels);
    }

    private static uint BodyColor(Mood mood) => mood switch
    {
        Mood.Happy => 0xFF4CAF50,
        Mood.Eating => 0xFFFFC107,
        Mood.Full => 0xFFFF7043,
        Mood.Stressed => 0xFFE53935,
        Mood.NoSession => 0xFF9E9E9E,
        _ => 0xFF9E9E9E
    };

    private static PixelFrame GenerateFrame(uint bodyColor, Mood mood, bool squish, bool isWorking, bool isWorried)
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, Transparent);

        int top = squish ? 3 : 2;
        int bottom = Size - 2;
        int left = 2;
        int right = Size - 3;

        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                pixels[y * Size + x] = bodyColor;

        int eyeY = top + 3;
        if (mood == Mood.NoSession)
        {
            pixels[eyeY * Size + 5] = EyeColor;
            pixels[eyeY * Size + 6] = EyeColor;
            pixels[eyeY * Size + 9] = EyeColor;
            pixels[eyeY * Size + 10] = EyeColor;
        }
        else if (mood == Mood.Stressed)
        {
            pixels[eyeY * Size + 5] = EyeColor;
            pixels[(eyeY + 1) * Size + 6] = EyeColor;
            pixels[(eyeY + 1) * Size + 9] = EyeColor;
            pixels[eyeY * Size + 10] = EyeColor;
        }
        else
        {
            pixels[eyeY * Size + 5] = EyeColor;
            pixels[eyeY * Size + 10] = EyeColor;
        }

        if (isWorking)
            CopyOpaquePixels(pixels, GenerateWorkingOverlayFrame(squish));

        if (isWorried)
            CopyOpaquePixels(pixels, GenerateWorriedOverlayFrame(squish));

        return new PixelFrame(Size, Size, pixels);
    }

    private static void CopyOpaquePixels(uint[] destination, PixelFrame overlay)
    {
        for (int i = 0; i < destination.Length; i++)
            if ((overlay.Pixels[i] >> 24) != 0)
                destination[i] = overlay.Pixels[i];
    }
}

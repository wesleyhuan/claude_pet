using ClaudePet.Models;

namespace ClaudePet.Rendering;

public static class PixelArtGenerator
{
    private const int Size = 16;
    private const uint Transparent = 0x00000000;
    private const uint EyeColor = 0xFF212121;

    public static IReadOnlyList<PixelFrame> GenerateFrames(Mood mood)
    {
        var bodyColor = BodyColor(mood);
        return new[]
        {
            GenerateFrame(bodyColor, mood, squish: false),
            GenerateFrame(bodyColor, mood, squish: true)
        };
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

    private static PixelFrame GenerateFrame(uint bodyColor, Mood mood, bool squish)
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

        return new PixelFrame(Size, Size, pixels);
    }
}

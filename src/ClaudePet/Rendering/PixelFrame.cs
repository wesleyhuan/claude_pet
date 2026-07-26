namespace ClaudePet.Rendering;

public sealed record PixelFrame(int Width, int Height, uint[] Pixels)
{
    public uint this[int x, int y] => Pixels[y * Width + x];
}

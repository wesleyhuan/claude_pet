using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class PngFrameCodec
{
    private const int Size = 16;

    public static PixelFrame? Decode(byte[] pngBytes, Action<string>? onError = null)
    {
        BitmapSource decoded;
        try
        {
            using var stream = new MemoryStream(pngBytes);
            decoded = BitmapDecoder.Create(
                stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException)
        {
            onError?.Invoke("not a valid PNG file");
            return null;
        }

        if (decoded.PixelWidth != Size || decoded.PixelHeight != Size)
        {
            onError?.Invoke($"PNG must be exactly {Size}x{Size}, found {decoded.PixelWidth}x{decoded.PixelHeight}");
            return null;
        }

        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        var stride = Size * 4;
        var bytes = new byte[stride * Size];
        converted.CopyPixels(bytes, stride, 0);

        var pixels = new uint[Size * Size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = BitConverter.ToUInt32(bytes, i * 4);

        return new PixelFrame(Size, Size, pixels);
    }

    public static byte[] Encode(PixelFrame frame)
    {
        var stride = frame.Width * 4;
        var bytes = new byte[stride * frame.Height];
        for (int i = 0; i < frame.Pixels.Length; i++)
            BitConverter.GetBytes(frame.Pixels[i]).CopyTo(bytes, i * 4);

        var bitmap = BitmapSource.Create(
            frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}

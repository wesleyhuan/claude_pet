using System.Text;
using System.Text.Json;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

// Inverse of PixelGridParser - writes a PixelFrame as a JSON pixel grid,
// auto-deriving a minimal palette from the frame's own distinct colors.
// Used only to seed ExampleSkinGenerator's starter skin, whose frames use a
// handful of colors at most, so the single-character palette-key space
// ('A'.."Z", then continuing past 'Z') is never a practical concern here.
public static class PixelGridWriter
{
    public static string Write(PixelFrame frame)
    {
        var palette = new Dictionary<uint, char>();
        var nextKey = 'A';
        var rows = new string[frame.Height];

        for (int y = 0; y < frame.Height; y++)
        {
            var row = new StringBuilder(frame.Width);
            for (int x = 0; x < frame.Width; x++)
            {
                var color = frame[x, y];
                if (!palette.TryGetValue(color, out var key))
                {
                    key = nextKey;
                    nextKey = (char)(nextKey + 1);
                    palette[color] = key;
                }
                row.Append(key);
            }
            rows[y] = row.ToString();
        }

        var paletteDto = palette.ToDictionary(kv => kv.Value.ToString(), kv => kv.Key.ToString("X8"));
        var dto = new { palette = paletteDto, pixels = rows };
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }
}

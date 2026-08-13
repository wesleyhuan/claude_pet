using System.Globalization;
using System.Text.Json;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class PixelGridParser
{
    private const int Size = 16;

    public static PixelFrame? Parse(string json, Action<string>? onError = null)
    {
        GridDto? dto;
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            dto = JsonSerializer.Deserialize<GridDto>(json, options);
        }
        catch (JsonException)
        {
            onError?.Invoke("pixel grid is not valid JSON");
            return null;
        }

        if (dto?.Palette is null || dto.Pixels is null)
        {
            onError?.Invoke("pixel grid missing palette or pixels");
            return null;
        }

        if (dto.Pixels.Length != Size)
        {
            onError?.Invoke($"pixel grid must have exactly {Size} rows, found {dto.Pixels.Length}");
            return null;
        }

        var palette = new Dictionary<char, uint>();
        foreach (var (key, hex) in dto.Palette)
        {
            if (key.Length != 1)
            {
                onError?.Invoke($"palette key '{key}' must be a single character");
                return null;
            }
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
            {
                onError?.Invoke($"palette entry '{key}' has an invalid hex color '{hex}'");
                return null;
            }
            palette[key[0]] = color;
        }

        var pixels = new uint[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            var row = dto.Pixels[y];
            if (row.Length != Size)
            {
                onError?.Invoke($"pixel grid row {y} must have exactly {Size} characters, found {row.Length}");
                return null;
            }
            for (int x = 0; x < Size; x++)
            {
                if (!palette.TryGetValue(row[x], out var color))
                {
                    onError?.Invoke($"pixel grid character '{row[x]}' at row {y}, column {x} has no palette entry");
                    return null;
                }
                pixels[y * Size + x] = color;
            }
        }

        return new PixelFrame(Size, Size, pixels);
    }

    private sealed class GridDto
    {
        public Dictionary<string, string>? Palette { get; set; }
        public string[]? Pixels { get; set; }
    }
}

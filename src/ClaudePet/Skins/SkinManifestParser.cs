using System.Text.Json;
using ClaudePet.Models;

namespace ClaudePet.Skins;

public sealed record SkinFramePaths(string Frame0, string Frame1);

public sealed record SkinManifest(
    string DisplayName,
    IReadOnlyDictionary<Mood, SkinFramePaths> Moods,
    SkinFramePaths WorkingOverlay,
    SkinFramePaths WorriedOverlay);

public static class SkinManifestParser
{
    private static readonly (Mood Mood, string Key)[] RequiredMoods =
    {
        (Mood.NoSession, "NoSession"),
        (Mood.Happy, "Happy"),
        (Mood.Eating, "Eating"),
        (Mood.Full, "Full"),
        (Mood.Stressed, "Stressed"),
    };

    public static SkinManifest? Parse(string json, Action<string>? onError = null)
    {
        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            onError?.Invoke("skin.json is not valid JSON");
            return null;
        }

        if (dto is null)
        {
            onError?.Invoke("skin.json is empty");
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            onError?.Invoke("skin.json missing displayName");
            return null;
        }

        if (dto.Moods is null)
        {
            onError?.Invoke("skin.json missing moods");
            return null;
        }

        var moods = new Dictionary<Mood, SkinFramePaths>();
        foreach (var (mood, key) in RequiredMoods)
        {
            if (!dto.Moods.TryGetValue(key, out var paths) || !TryToFramePaths(paths, out var framePaths))
            {
                onError?.Invoke($"skin.json missing or incomplete moods.{key}");
                return null;
            }
            moods[mood] = framePaths;
        }

        if (dto.Overlays is null || !dto.Overlays.TryGetValue("working", out var workingDto) ||
            !TryToFramePaths(workingDto, out var working))
        {
            onError?.Invoke("skin.json missing or incomplete overlays.working");
            return null;
        }

        if (!dto.Overlays.TryGetValue("worried", out var worriedDto) || !TryToFramePaths(worriedDto, out var worried))
        {
            onError?.Invoke("skin.json missing or incomplete overlays.worried");
            return null;
        }

        return new SkinManifest(dto.DisplayName, moods, working, worried);
    }

    private static bool TryToFramePaths(FramePathsDto? dto, out SkinFramePaths framePaths)
    {
        if (dto is not null && !string.IsNullOrWhiteSpace(dto.Frame0) && !string.IsNullOrWhiteSpace(dto.Frame1))
        {
            framePaths = new SkinFramePaths(dto.Frame0, dto.Frame1);
            return true;
        }
        framePaths = null!;
        return false;
    }

    private sealed class ManifestDto
    {
        public string? DisplayName { get; set; }
        public Dictionary<string, FramePathsDto>? Moods { get; set; }
        public Dictionary<string, FramePathsDto>? Overlays { get; set; }
    }

    private sealed class FramePathsDto
    {
        public string? Frame0 { get; set; }
        public string? Frame1 { get; set; }
    }
}

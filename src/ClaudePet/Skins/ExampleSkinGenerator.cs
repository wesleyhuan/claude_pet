using System.IO;
using System.Text.Json;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class ExampleSkinGenerator
{
    // A couple of moods are written as PNG and the rest (plus both overlays)
    // as JSON grids, purely to demonstrate that a skin can freely mix both
    // frame formats - see docs/superpowers/specs/2026-08-13-pet-skin-system-design.md.
    private static readonly Mood[] PngMoods = { Mood.NoSession, Mood.Happy };
    private static readonly Mood[] JsonMoods = { Mood.Eating, Mood.Full, Mood.Stressed };

    public static void EnsureExampleSkin(string skinsRoot, DebugLog log)
    {
        var exampleDir = Path.Combine(skinsRoot, "example");
        if (Directory.Exists(exampleDir))
            return;

        try
        {
            Directory.CreateDirectory(exampleDir);

            var moodPaths = new Dictionary<string, object>();
            foreach (var mood in PngMoods)
                moodPaths[mood.ToString()] = WriteMoodPng(exampleDir, mood);
            foreach (var mood in JsonMoods)
                moodPaths[mood.ToString()] = WriteMoodJson(exampleDir, mood);

            var overlays = new Dictionary<string, object>
            {
                ["working"] = WriteOverlayJson(exampleDir, "working", PixelArtGenerator.GenerateWorkingOverlayFrame),
                ["worried"] = WriteOverlayJson(exampleDir, "worried", PixelArtGenerator.GenerateWorriedOverlayFrame),
            };

            var manifest = new
            {
                displayName = "Example (copy me!)",
                moods = moodPaths,
                overlays,
            };

            File.WriteAllText(
                Path.Combine(exampleDir, "skin.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write($"ExampleSkinGenerator: failed to write example skin: {ex.Message}");
        }
    }

    private static object WriteMoodPng(string dir, Mood mood)
    {
        var frames = PixelArtGenerator.GenerateFrames(mood);
        var name = mood.ToString().ToLowerInvariant();
        var frame0Name = $"{name}_0.png";
        var frame1Name = $"{name}_1.png";
        File.WriteAllBytes(Path.Combine(dir, frame0Name), PngFrameCodec.Encode(frames[0]));
        File.WriteAllBytes(Path.Combine(dir, frame1Name), PngFrameCodec.Encode(frames[1]));
        return new { frame0 = frame0Name, frame1 = frame1Name };
    }

    private static object WriteMoodJson(string dir, Mood mood)
    {
        var frames = PixelArtGenerator.GenerateFrames(mood);
        var name = mood.ToString().ToLowerInvariant();
        var frame0Name = $"{name}_0.json";
        var frame1Name = $"{name}_1.json";
        File.WriteAllText(Path.Combine(dir, frame0Name), PixelGridWriter.Write(frames[0]));
        File.WriteAllText(Path.Combine(dir, frame1Name), PixelGridWriter.Write(frames[1]));
        return new { frame0 = frame0Name, frame1 = frame1Name };
    }

    private static object WriteOverlayJson(string dir, string name, Func<bool, PixelFrame> generate)
    {
        var frame0Name = $"{name}_0.json";
        var frame1Name = $"{name}_1.json";
        File.WriteAllText(Path.Combine(dir, frame0Name), PixelGridWriter.Write(generate(false)));
        File.WriteAllText(Path.Combine(dir, frame1Name), PixelGridWriter.Write(generate(true)));
        return new { frame0 = frame0Name, frame1 = frame1Name };
    }
}

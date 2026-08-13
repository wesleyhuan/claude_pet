using System.IO;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public sealed class SkinLoader
{
    private readonly string _skinsRoot;
    private readonly DebugLog _log;

    public SkinLoader(string skinsRoot, DebugLog log)
    {
        _skinsRoot = skinsRoot;
        _log = log;
    }

    public IReadOnlyList<Skin> DiscoverSkins()
    {
        var skins = new List<Skin>();
        if (!Directory.Exists(_skinsRoot))
            return skins;

        foreach (var dir in Directory.EnumerateDirectories(_skinsRoot))
        {
            var folderName = Path.GetFileName(dir);
            var skin = TryLoadSkin(dir, folderName);
            if (skin is not null)
                skins.Add(skin);
        }

        return skins;
    }

    private Skin? TryLoadSkin(string dir, string folderName)
    {
        var manifestPath = Path.Combine(dir, "skin.json");
        if (!File.Exists(manifestPath))
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - skin.json not found");
            return null;
        }

        string manifestJson;
        try
        {
            manifestJson = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - failed to read skin.json: {ex.Message}");
            return null;
        }

        string? manifestError = null;
        var manifest = SkinManifestParser.Parse(manifestJson, err => manifestError = err);
        if (manifest is null)
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - {manifestError}");
            return null;
        }

        var moods = new Dictionary<Mood, (PixelFrame, PixelFrame)>();
        foreach (var (mood, paths) in manifest.Moods)
        {
            var frame0 = LoadFrame(dir, paths.Frame0, folderName);
            var frame1 = LoadFrame(dir, paths.Frame1, folderName);
            if (frame0 is null || frame1 is null)
                return null;
            moods[mood] = (frame0, frame1);
        }

        var workingFrame0 = LoadFrame(dir, manifest.WorkingOverlay.Frame0, folderName);
        var workingFrame1 = LoadFrame(dir, manifest.WorkingOverlay.Frame1, folderName);
        var worriedFrame0 = LoadFrame(dir, manifest.WorriedOverlay.Frame0, folderName);
        var worriedFrame1 = LoadFrame(dir, manifest.WorriedOverlay.Frame1, folderName);
        if (workingFrame0 is null || workingFrame1 is null || worriedFrame0 is null || worriedFrame1 is null)
            return null;

        return new Skin(
            folderName,
            manifest.DisplayName,
            moods,
            (workingFrame0, workingFrame1),
            (worriedFrame0, worriedFrame1));
    }

    private PixelFrame? LoadFrame(string skinDir, string relativePath, string folderName)
    {
        var path = Path.Combine(skinDir, relativePath);
        if (!File.Exists(path))
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - frame file not found: {relativePath}");
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension == ".png")
            {
                var bytes = File.ReadAllBytes(path);
                string? error = null;
                var frame = PngFrameCodec.Decode(bytes, err => error = err);
                if (frame is null)
                    _log.Write($"SkinLoader: skipping '{folderName}' - {relativePath}: {error}");
                return frame;
            }

            if (extension == ".json")
            {
                var text = File.ReadAllText(path);
                string? error = null;
                var frame = PixelGridParser.Parse(text, err => error = err);
                if (frame is null)
                    _log.Write($"SkinLoader: skipping '{folderName}' - {relativePath}: {error}");
                return frame;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - failed to read {relativePath}: {ex.Message}");
            return null;
        }

        _log.Write($"SkinLoader: skipping '{folderName}' - unrecognized frame file extension: {relativePath}");
        return null;
    }
}

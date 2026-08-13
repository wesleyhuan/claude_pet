using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public sealed class Skin
{
    public string FolderName { get; }
    public string DisplayName { get; }

    private readonly IReadOnlyDictionary<Mood, (PixelFrame Frame0, PixelFrame Frame1)> _moods;
    private readonly (PixelFrame Frame0, PixelFrame Frame1) _working;
    private readonly (PixelFrame Frame0, PixelFrame Frame1) _worried;

    // _moods is required to contain all 5 Mood values - SkinManifestParser
    // guarantees this for any manifest it successfully parses, and SkinLoader
    // never constructs a Skin from a manifest that failed to parse or from
    // frame files that failed to load.
    public Skin(
        string folderName,
        string displayName,
        IReadOnlyDictionary<Mood, (PixelFrame Frame0, PixelFrame Frame1)> moods,
        (PixelFrame Frame0, PixelFrame Frame1) working,
        (PixelFrame Frame0, PixelFrame Frame1) worried)
    {
        FolderName = folderName;
        DisplayName = displayName;
        _moods = moods;
        _working = working;
        _worried = worried;
    }

    public IReadOnlyList<PixelFrame> GenerateFrames(Mood mood, bool isWorking = false, bool isWorried = false)
    {
        var (frame0, frame1) = _moods[mood];

        if (isWorking)
        {
            frame0 = PixelCompositor.CompositeOver(frame0, _working.Frame0);
            frame1 = PixelCompositor.CompositeOver(frame1, _working.Frame1);
        }

        if (isWorried)
        {
            frame0 = PixelCompositor.CompositeOver(frame0, _worried.Frame0);
            frame1 = PixelCompositor.CompositeOver(frame1, _worried.Frame1);
        }

        return new[] { frame0, frame1 };
    }
}

using ClaudePet.Models;
using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class SkinTests
{
    private static PixelFrame SolidFrame(uint color)
    {
        var pixels = new uint[16 * 16];
        Array.Fill(pixels, color);
        return new PixelFrame(16, 16, pixels);
    }

    private static PixelFrame TransparentFrame()
    {
        return new PixelFrame(16, 16, new uint[16 * 16]);
    }

    private static Skin BuildSkin(
        (PixelFrame, PixelFrame)? working = null,
        (PixelFrame, PixelFrame)? worried = null)
    {
        var happyFrames = (SolidFrame(0xFF4CAF50), SolidFrame(0xFF388E3C));
        var moods = new Dictionary<Mood, (PixelFrame, PixelFrame)>
        {
            [Mood.NoSession] = (SolidFrame(0xFF9E9E9E), SolidFrame(0xFF9E9E9E)),
            [Mood.Happy] = happyFrames,
            [Mood.Eating] = (SolidFrame(0xFFFFC107), SolidFrame(0xFFFFC107)),
            [Mood.Full] = (SolidFrame(0xFFFF7043), SolidFrame(0xFFFF7043)),
            [Mood.Stressed] = (SolidFrame(0xFFE53935), SolidFrame(0xFFE53935)),
        };

        return new Skin(
            "my-skin",
            "My Skin",
            moods,
            working ?? (TransparentFrame(), TransparentFrame()),
            worried ?? (TransparentFrame(), TransparentFrame()));
    }

    [Fact]
    public void GenerateFrames_NoOverlays_ReturnsTwoMoodFrames()
    {
        var skin = BuildSkin();

        var frames = skin.GenerateFrames(Mood.Happy);

        Assert.Equal(2, frames.Count);
        Assert.Equal(0xFF4CAF50u, frames[0][0, 0]);
        Assert.Equal(0xFF388E3Cu, frames[1][0, 0]);
    }

    [Fact]
    public void GenerateFrames_IsWorking_CompositesWorkingOverlayOnTop()
    {
        var workingOverlay = (SolidFrame(0xFF29B6F6), SolidFrame(0xFF29B6F6));
        var skin = BuildSkin(working: workingOverlay);

        var frames = skin.GenerateFrames(Mood.Happy, isWorking: true);

        Assert.Equal(0xFF29B6F6u, frames[0][0, 0]);
    }

    [Fact]
    public void GenerateFrames_IsWorried_CompositesWorriedOverlayOnTop()
    {
        var worriedOverlay = (SolidFrame(0xFF81D4FA), SolidFrame(0xFF81D4FA));
        var skin = BuildSkin(worried: worriedOverlay);

        var frames = skin.GenerateFrames(Mood.Happy, isWorried: true);

        Assert.Equal(0xFF81D4FAu, frames[0][0, 0]);
    }

    [Fact]
    public void GenerateFrames_NeitherOverlayFlagSet_IgnoresOverlayFrames()
    {
        var workingOverlay = (SolidFrame(0xFF29B6F6), SolidFrame(0xFF29B6F6));
        var skin = BuildSkin(working: workingOverlay);

        var frames = skin.GenerateFrames(Mood.Happy);

        Assert.Equal(0xFF4CAF50u, frames[0][0, 0]);
    }

    [Fact]
    public void FolderNameAndDisplayName_ExposedAsConstructed()
    {
        var skin = BuildSkin();

        Assert.Equal("my-skin", skin.FolderName);
        Assert.Equal("My Skin", skin.DisplayName);
    }
}

using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Tests;

public class PixelArtGeneratorTests
{
    [Fact]
    public void GenerateFrames_ReturnsExactlyTwoFrames()
    {
        var frames = PixelArtGenerator.GenerateFrames(Mood.Happy);

        Assert.Equal(2, frames.Count);
    }

    [Theory]
    [InlineData(Mood.Happy)]
    [InlineData(Mood.Eating)]
    [InlineData(Mood.Full)]
    [InlineData(Mood.Stressed)]
    [InlineData(Mood.NoSession)]
    public void GenerateFrames_AllFramesAre16x16(Mood mood)
    {
        foreach (var frame in PixelArtGenerator.GenerateFrames(mood))
        {
            Assert.Equal(16, frame.Width);
            Assert.Equal(16, frame.Height);
        }
    }

    [Theory]
    [InlineData(Mood.Happy)]
    [InlineData(Mood.Eating)]
    [InlineData(Mood.Full)]
    [InlineData(Mood.Stressed)]
    [InlineData(Mood.NoSession)]
    public void GenerateFrames_CenterPixelIsOpaqueBodyColor(Mood mood)
    {
        var frame = PixelArtGenerator.GenerateFrames(mood)[0];

        var centerPixel = frame[8, 8];

        Assert.Equal(0xFFu, centerPixel >> 24); // fully opaque alpha byte
    }

    [Fact]
    public void GenerateFrames_DifferentMoodsHaveDifferentBodyColors()
    {
        var happy = PixelArtGenerator.GenerateFrames(Mood.Happy)[0][8, 8];
        var stressed = PixelArtGenerator.GenerateFrames(Mood.Stressed)[0][8, 8];

        Assert.NotEqual(happy, stressed);
    }

    [Fact]
    public void GenerateFrames_SecondFrameIsSquishedRelativeToFirst()
    {
        var frames = PixelArtGenerator.GenerateFrames(Mood.Happy);

        // Row 2 (y=2) is part of the body in frame 0 (top starts at 2) but
        // transparent in frame 1 (top starts at 3, the "squish" frame).
        var topRowFrame0 = frames[0][8, 2];
        var topRowFrame1 = frames[1][8, 2];

        Assert.NotEqual(0u, topRowFrame0 >> 24);
        Assert.Equal(0u, topRowFrame1 >> 24);
    }

    [Fact]
    public void GenerateFrames_NoOverlays_TopRightAndTopLeftMatchBodyColor()
    {
        var frame = PixelArtGenerator.GenerateFrames(Mood.Happy)[0];
        var bodyColor = frame[8, 8];

        Assert.Equal(bodyColor, frame[12, 2]);
        Assert.Equal(bodyColor, frame[2, 2]);
    }

    [Fact]
    public void GenerateFrames_IsWorking_AddsAccentPixelNearTopRight()
    {
        var frame = PixelArtGenerator.GenerateFrames(Mood.Happy, isWorking: true)[0];
        var bodyColor = PixelArtGenerator.GenerateFrames(Mood.Happy)[0][12, 2];

        Assert.NotEqual(bodyColor, frame[12, 2]);
    }

    [Fact]
    public void GenerateFrames_IsWorried_AddsSweatDropNearTopLeft()
    {
        var frame = PixelArtGenerator.GenerateFrames(Mood.Happy, isWorried: true)[0];
        var bodyColor = PixelArtGenerator.GenerateFrames(Mood.Happy)[0][2, 2];

        Assert.NotEqual(bodyColor, frame[2, 2]);
    }

    [Fact]
    public void GenerateFrames_WorkingAndWorried_BothOverlaysCoexist()
    {
        var frame = PixelArtGenerator.GenerateFrames(Mood.Happy, isWorking: true, isWorried: true)[0];
        var plain = PixelArtGenerator.GenerateFrames(Mood.Happy)[0];

        Assert.NotEqual(plain[12, 2], frame[12, 2]);
        Assert.NotEqual(plain[2, 2], frame[2, 2]);
    }

    [Fact]
    public void GenerateFrames_IsWorking_SparkPositionAlternatesBetweenFrames()
    {
        var frames = PixelArtGenerator.GenerateFrames(Mood.Happy, isWorking: true);

        // Frame 0 (not squished, top=2): spark at x=12. Frame 1 (squished,
        // top=3): spark at x=11, and frame 1's own top row (y=2) is outside
        // the squished body, so x=12 goes back to transparent there.
        Assert.NotEqual(0u, frames[0][12, 2] >> 24);
        Assert.Equal(0u, frames[1][12, 2] >> 24);
        Assert.NotEqual(0u, frames[1][11, 3] >> 24);
    }
}

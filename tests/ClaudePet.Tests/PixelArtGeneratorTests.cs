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
}

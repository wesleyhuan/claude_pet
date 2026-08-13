using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class ExampleSkinGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetExampleSkinTests_" + Guid.NewGuid());

    public ExampleSkinGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private DebugLog Log() => new(Path.Combine(_dir, "debug.log"));

    [Fact]
    public void EnsureExampleSkin_CreatesLoadableValidSkin()
    {
        var log = Log();

        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);
        var skins = new SkinLoader(_dir, log).DiscoverSkins();

        Assert.Single(skins);
        Assert.Equal("example", skins[0].FolderName);
        Assert.Equal("Example (copy me!)", skins[0].DisplayName);
    }

    [Fact]
    public void EnsureExampleSkin_GeneratedSkin_ProducesTwo16x16FramesForEveryMood()
    {
        var log = Log();
        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);
        var skin = new SkinLoader(_dir, log).DiscoverSkins()[0];

        foreach (Mood mood in Enum.GetValues<Mood>())
        {
            var frames = skin.GenerateFrames(mood);
            Assert.Equal(2, frames.Count);
            Assert.Equal(16, frames[0].Width);
            Assert.Equal(16, frames[0].Height);
        }
    }

    [Fact]
    public void EnsureExampleSkin_AlreadyExists_DoesNotOverwriteOrThrow()
    {
        var log = Log();
        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);
        var exampleDir = Path.Combine(_dir, "example");
        var manifestPath = Path.Combine(exampleDir, "skin.json");
        var originalWriteTime = File.GetLastWriteTimeUtc(manifestPath);

        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);

        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(manifestPath));
    }
}

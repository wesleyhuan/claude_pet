using ClaudePet.Logging;

namespace ClaudePet.Tests;

public class DebugLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetLogTests_" + Guid.NewGuid());

    public DebugLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string FilePath => Path.Combine(_dir, "nested", "debug.log");

    [Fact]
    public void Constructor_CreatesParentDirectory()
    {
        _ = new DebugLog(FilePath);

        Assert.True(Directory.Exists(Path.GetDirectoryName(FilePath)));
    }

    [Fact]
    public void Write_AppendsMessageWithTimestamp()
    {
        var log = new DebugLog(FilePath);

        log.Write("something went wrong");

        var content = File.ReadAllText(FilePath);
        Assert.Contains("something went wrong", content);
    }

    [Fact]
    public void Write_CalledTwice_AppendsBothLinesWithoutTruncating()
    {
        var log = new DebugLog(FilePath);

        log.Write("first");
        log.Write("second");

        var lines = File.ReadAllLines(FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("first", lines[0]);
        Assert.Contains("second", lines[1]);
    }
}

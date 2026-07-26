using ClaudePet.Services;

namespace ClaudePet.Tests;

public class TailReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetTailTests_" + Guid.NewGuid());

    public TailReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string NewFile(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadNewLines_FreshFileSmallerThanLookback_ReturnsAllLines()
    {
        var path = NewFile("line1\nline2\n");
        var reader = new TailReader(initialLookbackBytes: 65536);

        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "line1", "line2" }, lines);
    }

    [Fact]
    public void ReadNewLines_CalledAgainWithNoNewContent_ReturnsEmpty()
    {
        var path = NewFile("line1\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(path);

        var lines = reader.ReadNewLines(path);

        Assert.Empty(lines);
    }

    [Fact]
    public void ReadNewLines_AfterAppend_ReturnsOnlyNewLines()
    {
        var path = NewFile("line1\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(path);

        File.AppendAllText(path, "line2\nline3\n");
        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "line2", "line3" }, lines);
    }

    [Fact]
    public void ReadNewLines_SwitchingToDifferentFile_AppliesLookbackFromNewFile()
    {
        var pathA = NewFile("a-line1\n");
        var pathB = NewFile("b-line1\nb-line2\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(pathA);

        var lines = reader.ReadNewLines(pathB);

        Assert.Equal(new[] { "b-line1", "b-line2" }, lines);
    }

    [Fact]
    public void ReadNewLines_SwitchingToFileLargerThanLookback_DropsPartialFirstLine()
    {
        // "AAAA\nBBBB\nCCCC\n" is 15 bytes; a lookback of 6 starts mid "BBBB\n",
        // so the (partial) first captured line must be dropped.
        var path = NewFile("AAAA\nBBBB\nCCCC\n");
        var reader = new TailReader(initialLookbackBytes: 6);

        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "CCCC" }, lines);
    }

    [Fact]
    public void ReadNewLines_FileTruncated_RestartsFromBeginning()
    {
        var path = NewFile("this-is-a-long-first-line\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(path);

        File.WriteAllText(path, "short\n");
        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "short" }, lines);
    }

    [Fact]
    public void ReadNewLines_LookbackLandsExactlyOnLineBoundary_DoesNotDropTheLine()
    {
        // "AAAA\nCCCC\nDDDD\n" is 15 bytes; a lookback of 10 starts at byte 5,
        // which is exactly the first byte of "CCCC" (a clean line start, not
        // a partial line) — "CCCC" must NOT be dropped.
        var path = NewFile("AAAA\nCCCC\nDDDD\n");
        var reader = new TailReader(initialLookbackBytes: 10);

        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "CCCC", "DDDD" }, lines);
    }
}

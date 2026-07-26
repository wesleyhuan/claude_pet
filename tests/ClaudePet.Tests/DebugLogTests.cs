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

    [Fact]
    public void Write_LogFileExceedsSizeCap_RotatesToSingleBackupAndStartsFresh()
    {
        var log = new DebugLog(FilePath);
        var big = new string('x', 1024 * 1024 + 100); // > 1 MB cap

        log.Write(big);
        log.Write("after rotation");

        var backupPath = FilePath + ".1";
        Assert.True(File.Exists(backupPath));
        Assert.Contains("xxxxx", File.ReadAllText(backupPath));

        var mainContent = File.ReadAllText(FilePath);
        Assert.Contains("after rotation", mainContent);
        Assert.DoesNotContain("xxxxx", mainContent);
    }

    [Fact]
    public void Write_LogFileExceedsSizeCapTwice_OverwritesExistingBackup()
    {
        var log = new DebugLog(FilePath);
        var big = new string('x', 1024 * 1024 + 100);

        log.Write(big);
        log.Write(new string('y', 1024 * 1024 + 100));
        log.Write("final");

        var backupPath = FilePath + ".1";
        var backupContent = File.ReadAllText(backupPath);
        Assert.DoesNotContain("xxxxx", backupContent); // the first backup was overwritten
        Assert.Contains("yyyyy", backupContent);

        Assert.Contains("final", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Write_LogFileDirectoryInaccessible_DoesNotThrow()
    {
        var log = new DebugLog(FilePath);
        var nestedDir = Path.GetDirectoryName(FilePath)!;
        Directory.Delete(nestedDir, recursive: true);
        // Replace the directory with a file of the same name, so writing to
        // FilePath (a path under it) fails - simulates an inaccessible/unwritable
        // log location without relying on OS-specific ACL manipulation.
        File.WriteAllText(nestedDir, "occupying the directory's path");

        var exception = Record.Exception(() => log.Write("this must not throw"));

        Assert.Null(exception);
    }
}

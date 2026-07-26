using ClaudePet.Services;

namespace ClaudePet.Tests;

public class SessionLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClaudePetTests_" + Guid.NewGuid());

    public SessionLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteFile(string relativePath, DateTime lastWriteUtc)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "{}");
        File.SetLastWriteTimeUtc(fullPath, lastWriteUtc);
        return fullPath;
    }

    [Fact]
    public void FindActiveSessionFile_NoDirectory_ReturnsNull()
    {
        var result = SessionLocator.FindActiveSessionFile(Path.Combine(_root, "does-not-exist"));
        Assert.Null(result);
    }

    [Fact]
    public void FindActiveSessionFile_NoJsonlFiles_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_root, "proj"));
        Assert.Null(SessionLocator.FindActiveSessionFile(_root));
    }

    [Fact]
    public void FindActiveSessionFile_PicksMostRecentlyModified()
    {
        WriteFile(@"projA\older.jsonl", DateTime.UtcNow.AddMinutes(-10));
        var newer = WriteFile(@"projB\newer.jsonl", DateTime.UtcNow);

        var result = SessionLocator.FindActiveSessionFile(_root);

        Assert.Equal(newer, result);
    }

    [Fact]
    public void FindActiveSessionFile_ExcludesFilesUnderSubagentsFolder()
    {
        WriteFile(@"proj\subagents\agent-1.jsonl", DateTime.UtcNow);
        var topLevel = WriteFile(@"proj\session.jsonl", DateTime.UtcNow.AddMinutes(-5));

        var result = SessionLocator.FindActiveSessionFile(_root);

        Assert.Equal(topLevel, result);
    }
}

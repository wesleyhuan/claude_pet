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

    [Fact]
    public void FindActiveSessionFile_InaccessibleSubfolder_StillFindsFilesInAccessibleSiblings()
    {
        // Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) throws
        // UnauthorizedAccessException on the first unreadable subfolder it hits,
        // aborting the whole enumeration. Deny read/execute to the current user on a
        // subfolder via icacls to reliably reproduce that on Windows, then confirm
        // FindActiveSessionFile still finds the file in the accessible sibling folder
        // instead of throwing.
        var blockedDir = Path.Combine(_root, "blocked");
        WriteFile(@"blocked\a.jsonl", DateTime.UtcNow.AddMinutes(-1));
        var accessible = WriteFile(@"sibling\b.jsonl", DateTime.UtcNow);

        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        RunIcacls($"\"{blockedDir}\" /deny \"{user}:(RX)\"");

        try
        {
            var result = SessionLocator.FindActiveSessionFile(_root);
            Assert.Equal(accessible, result);
        }
        finally
        {
            // Must remove the deny ACE before Dispose() tries to recursively delete
            // _root, otherwise deleting "blocked" would itself throw.
            RunIcacls($"\"{blockedDir}\" /remove:d \"{user}\"");
        }
    }

    private static void RunIcacls(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("icacls", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit(10_000);
    }
}

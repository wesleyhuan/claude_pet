using System;
using System.IO;
using System.Linq;

namespace ClaudePet.Services;

public static class SessionLocator
{
    public static string? FindActiveSessionFile(string projectsRoot)
    {
        if (!Directory.Exists(projectsRoot))
            return null;

        return Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories)
            .Where(path => !IsUnderSubagentsFolder(path))
            .Select(path => new FileInfo(path))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Select(fi => fi.FullName)
            .FirstOrDefault();
    }

    private static bool IsUnderSubagentsFolder(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        return dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                  .Contains("subagents", StringComparer.OrdinalIgnoreCase);
    }
}

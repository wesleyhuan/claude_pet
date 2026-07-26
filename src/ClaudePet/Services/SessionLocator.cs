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

        // Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) throws
        // UnauthorizedAccessException on the first unreadable subfolder it encounters,
        // aborting the WHOLE enumeration - one inaccessible folder under
        // ~/.claude/projects would permanently break session lookup (UsageReader's
        // catch just means the same failure repeats on every refresh forever). The
        // EnumerationOptions overload with IgnoreInaccessible = true skips inaccessible
        // folders/files instead of aborting.
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        return Directory.EnumerateFiles(projectsRoot, "*.jsonl", enumerationOptions)
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

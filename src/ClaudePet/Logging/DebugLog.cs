using System;
using System.IO;

namespace ClaudePet.Logging;

public sealed class DebugLog
{
    // Keep rotation simple: one rollover backup, not a numbered series.
    private const long MaxSizeBytes = 1 * 1024 * 1024; // 1 MB

    private readonly string _filePath;
    private readonly object _lock = new();

    public DebugLog(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public void Write(string message)
    {
        var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";

        // A logger must never throw: if writing the log itself fails there is
        // nowhere further to report the error, and crashing the app that's
        // trying to report a DIFFERENT problem would make things strictly
        // worse. Swallow any failure here silently.
        try
        {
            lock (_lock)
            {
                RotateIfTooLarge();
                File.AppendAllText(_filePath, line);
            }
        }
        catch
        {
            // Intentionally discarded - see comment above.
        }
    }

    private void RotateIfTooLarge()
    {
        var info = new FileInfo(_filePath);
        if (!info.Exists || info.Length < MaxSizeBytes)
            return;

        var backupPath = _filePath + ".1";
        File.Delete(backupPath); // no-op if it doesn't exist; overwrites a stale backup
        File.Move(_filePath, backupPath);
    }
}

using System;
using System.IO;

namespace ClaudePet.Logging;

public sealed class DebugLog
{
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
        lock (_lock)
        {
            File.AppendAllText(_filePath, line);
        }
    }
}

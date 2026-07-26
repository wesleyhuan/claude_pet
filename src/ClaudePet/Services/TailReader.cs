using System;
using System.IO;
using System.Linq;

namespace ClaudePet.Services;

public sealed class TailReader
{
    private readonly int _initialLookbackBytes;
    private string? _currentPath;
    private long _position;

    public TailReader(int initialLookbackBytes = 65536)
    {
        _initialLookbackBytes = initialLookbackBytes;
    }

    public IReadOnlyList<string> ReadNewLines(string path)
    {
        bool isNewFile = !string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        bool startedMidFile = false;

        if (isNewFile)
        {
            _currentPath = path;
            var start = Math.Max(0, stream.Length - _initialLookbackBytes);
            startedMidFile = start > 0;
            _position = start;
        }
        else if (stream.Length < _position)
        {
            _position = 0;
        }

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        _position = stream.Position;

        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.TrimEnd('\r'))
                         .ToArray();

        return startedMidFile && lines.Length > 1 ? lines.Skip(1).ToArray() : lines;
    }
}

using System.IO;
using System.Text.Json;

namespace ClaudePet.Settings;


public sealed class SettingsStore
{
    private readonly string _filePath;
    private readonly Action<string>? _onError;

    public SettingsStore(string filePath, Action<string>? onError = null)
    {
        _filePath = filePath;
        _onError = onError;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _onError?.Invoke($"Failed to load settings from {_filePath}: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onError?.Invoke($"Failed to save settings to {_filePath}: {ex.Message}");
        }
    }
}

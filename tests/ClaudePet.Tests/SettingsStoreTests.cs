using ClaudePet.Settings;

namespace ClaudePet.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetSettingsTests_" + Guid.NewGuid());

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string FilePath => Path.Combine(_dir, "nested", "settings.json");

    [Fact]
    public void Load_FileDoesNotExist_ReturnsDefaults()
    {
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.Equal(-1, settings.WindowLeft);
        Assert.Equal(-1, settings.WindowTop);
        Assert.False(settings.RunAtStartup);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { WindowLeft = 100, WindowTop = 200, RunAtStartup = true };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        var store = new SettingsStore(FilePath);

        store.Save(new AppSettings());

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndReportsError()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not valid json ");
        string? reportedError = null;
        var store = new SettingsStore(FilePath, err => reportedError = err);

        var settings = store.Load();

        Assert.Equal(-1, settings.WindowLeft);
        Assert.NotNull(reportedError);
    }
}

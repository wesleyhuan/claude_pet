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

        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.False(settings.RunAtStartup);
        Assert.False(settings.ShowSubscriptionUsage);
        Assert.Null(settings.ActiveSkinName);
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
    public void SaveThenLoad_RoundTripsShowSubscriptionUsage()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { ShowSubscriptionUsage = true };

        store.Save(original);
        var loaded = store.Load();

        Assert.True(loaded.ShowSubscriptionUsage);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsActiveSkinName()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { ActiveSkinName = "my-cool-skin" };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("my-cool-skin", loaded.ActiveSkinName);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsNullWindowPosition()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { WindowLeft = null, WindowTop = null, RunAtStartup = false };

        store.Save(original);
        var loaded = store.Load();

        Assert.Null(loaded.WindowLeft);
        Assert.Null(loaded.WindowTop);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsRealNegativeWindowPosition()
    {
        // A saved position is legitimately negative on multi-monitor setups where a
        // monitor sits left of/above the primary - must round-trip distinctly from
        // "unset" (null), not collide with an old -1 sentinel.
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { WindowLeft = -1920, WindowTop = -200, RunAtStartup = false };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(-1920, loaded.WindowLeft);
        Assert.Equal(-200, loaded.WindowTop);
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

        Assert.Null(settings.WindowLeft);
        Assert.NotNull(reportedError);
    }
}

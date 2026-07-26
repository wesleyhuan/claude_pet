using System.IO;
using System.Windows;
using ClaudePet.Logging;
using ClaudePet.Services;
using ClaudePet.Settings;
using ClaudePet.Tray;

namespace ClaudePet;

public partial class App : System.Windows.Application
{
    private UsageReader? _usageReader;
    private TrayIconManager? _trayIconManager;
    private PetWindow? _petWindow;
    private MoodStateMachine? _moodStateMachine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudePet");
        var log = new DebugLog(Path.Combine(appDataDir, "debug.log"));
        var settingsStore = new SettingsStore(Path.Combine(appDataDir, "settings.json"), log.Write);

        _petWindow = new PetWindow(settingsStore);
        _petWindow.Show();

        _trayIconManager = new TrayIconManager(_petWindow, settingsStore, log);
        _moodStateMachine = new MoodStateMachine();

        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");

        _usageReader = new UsageReader(projectsRoot, log);
        _usageReader.UsageChanged += snapshot =>
        {
            Dispatcher.Invoke(() =>
            {
                var mood = _moodStateMachine.Update(snapshot);
                _petWindow.SetMood(mood);
            });
        };
        _usageReader.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _usageReader?.Dispose();
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}

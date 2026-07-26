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
            // BeginInvoke (not Invoke): this callback can run on a FileSystemWatcher
            // threadpool thread while the UI thread is synchronously blocked inside
            // Start() -> Refresh() waiting on UsageReader's internal lock. A blocking
            // Invoke here would deadlock (UI thread never pumps the dispatcher to
            // service it; this thread never returns to release anything the UI thread
            // is waiting on). Fire-and-forget is fine: nothing here needs to wait for
            // the UI update to complete before Refresh() continues.
            //
            // Also guard against posting to a dispatcher that has begun/finished
            // shutdown (e.g. a straggling watcher event after Application.Shutdown()),
            // which would otherwise throw and crash the watcher's threadpool callback.
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var mood = _moodStateMachine.Update(snapshot);
                    _petWindow.SetMood(mood);
                });
            }
            catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
            {
                log.Write($"UsageChanged handler: dispatcher unavailable, dropping update: {ex.Message}");
            }
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

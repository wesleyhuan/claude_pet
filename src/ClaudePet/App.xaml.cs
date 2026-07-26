using System.IO;
using System.Windows;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Services;
using ClaudePet.Settings;
using ClaudePet.Tray;

namespace ClaudePet;

public partial class App : System.Windows.Application
{
    // Named per-user-session mutex so a second manually-launched instance (e.g. a
    // user double-clicking the exe while "Run at startup" already has one
    // running) shuts itself down instead of creating a second tray icon/pet/
    // window and racing the first instance over the same settings/log files.
    // "Local\" (not "Global\"): this is a per-user tray app writing to per-user
    // %LOCALAPPDATA%, so the guard only needs to be scoped to the current
    // session, not machine-wide across every terminal-server/fast-user-switching
    // session on the box.
    private const string SingleInstanceMutexName = "Local\\ClaudePetSingleInstance";

    private UsageReader? _usageReader;
    private TrayIconManager? _trayIconManager;
    private PetWindow? _petWindow;
    private MoodStateMachine? _moodStateMachine;
    private System.Threading.Mutex? _singleInstanceMutex;
    private Mood? _lastAppliedMood;
    private DebugLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF dispatches OnStartup itself as a dispatcher operation, and
        // ShutdownMode is OnExplicitShutdown (see App.xaml) - nothing shuts the
        // app down automatically. If anything below throws before the window/tray
        // icon exist, letting it propagate to the DispatcherUnhandledException
        // handler registered below would just mark it handled and leave the
        // dispatcher pumping forever with no window, no tray icon, and no way for
        // the user to exit except killing the process - a silent, invisible
        // zombie. Wrapping the whole startup sequence here and explicitly calling
        // Shutdown() on failure turns that into a clean exit instead. This does
        // NOT change how exceptions during normal post-startup runtime are
        // handled - those still go through OnDispatcherUnhandledException /
        // OnAppDomainUnhandledException below, which remain in place and stay
        // recoverable.
        try
        {
            // Register global unhandled-exception handlers as early as possible -
            // before constructing anything else that could throw - so any failure
            // past this point at least gets reported instead of dying silently.
            // Handlers close over the nullable _log field rather than a local
            // variable, since DebugLog itself hasn't been constructed yet.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudePet");

            DebugLog log;
            try
            {
                log = new DebugLog(Path.Combine(appDataDir, "debug.log"));
            }
            catch (Exception)
            {
                // DebugLog's own construction (directory creation) could theoretically
                // throw. Write() itself can never throw once constructed (it guards its
                // own body), so falling back to a log under the temp directory - which is
                // about as reliable a writable location as exists on Windows - is enough
                // of a degraded path without over-engineering this further.
                log = new DebugLog(Path.Combine(Path.GetTempPath(), "ClaudePet", "debug.log"));
            }
            _log = log;

            bool acquiredMutex = true;
            try
            {
                _singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: false, SingleInstanceMutexName, out _);
            }
            catch (Exception ex)
            {
                // Mutex construction can throw (e.g. UnauthorizedAccessException from
                // a DACL conflict). Degrade gracefully rather than crashing on startup
                // over a guard that failed to construct - occasionally allowing two
                // instances is better than not starting at all.
                log.Write($"Failed to create single-instance mutex; proceeding without single-instance guard: {ex}");
                _singleInstanceMutex = null;
            }

            if (_singleInstanceMutex is not null)
            {
                try
                {
                    acquiredMutex = _singleInstanceMutex.WaitOne(TimeSpan.FromMilliseconds(500));
                }
                catch (AbandonedMutexException)
                {
                    // A previous instance terminated without releasing the mutex, but
                    // ownership is still granted to us - safe to proceed.
                    acquiredMutex = true;
                }
            }

            if (!acquiredMutex)
            {
                log.Write("Another Claude Pet instance is already running; shutting down this instance.");
                Shutdown();
                return;
            }

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
                        // MoodStateMachine.Update returns the current mood on every call,
                        // including when it hasn't changed. During an active session,
                        // watcher events fire far faster than the 500ms animation timer can
                        // advance frames, so calling SetMood unconditionally kept resetting
                        // _frameIndex to 0 and regenerating frames - freezing the bounce
                        // animation exactly when the app was busiest. Only re-apply when the
                        // mood band actually changed.
                        if (mood != _lastAppliedMood)
                        {
                            // Set _lastAppliedMood only after SetMood succeeds: if SetMood
                            // were to throw, the field must still reflect the last mood
                            // actually applied, so a later retry (e.g. the next snapshot
                            // carrying the same mood) doesn't skip re-applying it because
                            // the field already silently "moved on".
                            _petWindow.SetMood(mood);
                            _lastAppliedMood = mood;
                        }

                        // Unlike the mood band, the exact token count/percent can change
                        // on every snapshot even when the mood band doesn't, so this is
                        // updated unconditionally.
                        _trayIconManager.UpdateUsage(snapshot);
                    });
                }
                catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
                {
                    log.Write($"UsageChanged handler: dispatcher unavailable, dropping update: {ex.Message}");
                }
            };
            _usageReader.Start();
        }
        catch (Exception ex)
        {
            // Startup never completed - window/tray icon are not guaranteed to
            // exist. Log if a DebugLog was constructed in time, then shut down
            // explicitly instead of leaving a headless zombie process behind.
            _log?.Write($"Fatal exception during startup; shutting down: {ex}");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _usageReader?.Dispose();
        _trayIconManager?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (e.g. we shut down before ever acquiring it) - fine to skip.
            }
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Write($"Unhandled dispatcher exception: {e.Exception}");
        // Recoverable UI-thread exception - mark handled so it doesn't crash the
        // whole background app.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // .NET terminates the process regardless of anything done here when
        // e.IsTerminating is true - this can't be marked handled - but at least
        // logging it means the failure is visible after the fact.
        _log?.Write($"Unhandled AppDomain exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
    }
}

using System;
using System.IO;
using ClaudePet.Logging;
using ClaudePet.Models;

namespace ClaudePet.Services;

public sealed class UsageReader : IDisposable
{
    private const int DebounceMilliseconds = 300;

    private readonly string _projectsRoot;
    private readonly TailReader _tailReader = new();
    private readonly DebugLog _log;
    private readonly System.Timers.Timer _pollTimer;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _refreshLock = new();
    private string? _lastWarnedModel;
    private bool? _hadSession;

    public event Action<UsageSnapshot?>? UsageChanged;

    public UsageReader(string projectsRoot, DebugLog log)
    {
        _projectsRoot = projectsRoot;
        _log = log;

        _pollTimer = new System.Timers.Timer(5000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => Refresh();

        // Coalesce bursts of watcher events (a single write can fire several
        // Changed/Created events in quick succession) behind a ~300ms debounce
        // instead of doing a full recursive scan of every session file on every
        // raw event. AutoReset = false: each event restarts a one-shot timer, and
        // Refresh() only runs once no new event has arrived within the window.
        _debounceTimer = new System.Timers.Timer(DebounceMilliseconds) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => Refresh();

        if (Directory.Exists(_projectsRoot))
        {
            _watcher = new FileSystemWatcher(_projectsRoot)
            {
                IncludeSubdirectories = true,
                Filter = "*.jsonl",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _watcher.Changed += (_, _) => RestartDebounce();
            _watcher.Created += (_, _) => RestartDebounce();
            _watcher.EnableRaisingEvents = true;
        }
        else
        {
            _log.Write($"Projects root does not exist yet: {_projectsRoot}");
        }
    }

    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Start()
    {
        Refresh();
        _pollTimer.Start();
    }

    private void Refresh()
    {
        // The lock protects only the shared-state mutation below (TailReader /
        // SessionLocator calls, _lastWarnedModel, _hadSession). UsageChanged is
        // invoked AFTER the lock is released. Subscribers (e.g. App.xaml.cs) may
        // synchronously pump a dispatcher; invoking while holding this lock risked
        // a deadlock if the UI thread was itself blocked waiting on this same lock
        // (e.g. inside Start() -> Refresh()) while a background FileSystemWatcher
        // callback held the lock and tried to hand off to that same UI thread.
        bool invokeNoSession = false;
        UsageSnapshot? snapshotToInvoke = null;

        lock (_refreshLock)
        {
            try
            {
                var activePath = SessionLocator.FindActiveSessionFile(_projectsRoot);
                if (activePath is null)
                {
                    if (_hadSession != false)
                    {
                        _hadSession = false;
                        invokeNoSession = true;
                    }
                }
                else
                {
                    var lines = _tailReader.ReadNewLines(activePath);
                    if (lines.Count > 0)
                    {
                        var snapshot = UsageParser.ParseLatest(lines, model =>
                        {
                            if (_lastWarnedModel != model)
                            {
                                _log.Write($"Unknown model '{model}', falling back to default context limit.");
                                _lastWarnedModel = model;
                            }
                        });

                        if (snapshot is not null)
                        {
                            _hadSession = true;
                            snapshotToInvoke = snapshot;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Write($"UsageReader.Refresh failed reading active session file: {ex.Message}");
            }
        }

        if (invokeNoSession)
            UsageChanged?.Invoke(null);
        else if (snapshotToInvoke is not null)
            UsageChanged?.Invoke(snapshotToInvoke);
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _debounceTimer.Dispose();
        _watcher?.Dispose();
    }
}

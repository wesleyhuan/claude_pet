using System.Security;
using System.Windows;
using System.Windows.Forms;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Native;
using ClaudePet.Settings;

namespace ClaudePet.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PetWindow _petWindow;
    private readonly SettingsStore _settingsStore;
    private readonly DebugLog _log;
    private readonly ToolStripMenuItem _dragItem;
    private readonly ToolStripMenuItem _skinMenu;
    private readonly List<(ToolStripMenuItem Item, string? FolderName)> _skinItems = new();
    private bool _dragMode;
    private UsageSnapshot? _lastUsage;
    private RateLimitSnapshot? _lastRateLimit;
    private SubscriptionUsageSnapshot? _lastSubscriptionUsage;

    public event Action<bool>? SubscriptionUsageToggled;
    public event Action<string?>? SkinSelected;

    public TrayIconManager(PetWindow petWindow, SettingsStore settingsStore, DebugLog log)
    {
        _petWindow = petWindow;
        _settingsStore = settingsStore;
        _log = log;

        _dragItem = new ToolStripMenuItem("Enable dragging", null, ToggleDragMode);

        var runAtStartupItem = new ToolStripMenuItem("Run at startup", null, ToggleRunAtStartup)
        {
            Checked = _settingsStore.Load().RunAtStartup
        };

        var subscriptionUsageItem = new ToolStripMenuItem("Show subscription usage (unofficial)", null, ToggleSubscriptionUsage)
        {
            Checked = _settingsStore.Load().ShowSubscriptionUsage
        };

        _skinMenu = new ToolStripMenuItem("Skin");

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_dragItem);
        menu.Items.Add(runAtStartupItem);
        menu.Items.Add(subscriptionUsageItem);
        menu.Items.Add(_skinMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Claude Pet",
            ContextMenuStrip = menu
        };
    }

    public void UpdateUsage(UsageSnapshot? snapshot)
    {
        _lastUsage = snapshot;
        RefreshTooltip();
    }

    public void UpdateRateLimit(RateLimitSnapshot? snapshot)
    {
        _lastRateLimit = snapshot;
        RefreshTooltip();
    }

    public void UpdateSubscriptionUsage(SubscriptionUsageSnapshot? snapshot)
    {
        _lastSubscriptionUsage = snapshot;
        RefreshTooltip();
    }

    public void PopulateSkinMenu(IReadOnlyList<(string FolderName, string DisplayName)> skins, string? activeSkinName)
    {
        _skinMenu.DropDownItems.Clear();
        _skinItems.Clear();

        AddSkinItem("Default", null, activeSkinName);
        foreach (var (folderName, displayName) in skins)
            AddSkinItem(displayName, folderName, activeSkinName);
    }

    private void AddSkinItem(string label, string? folderName, string? activeSkinName)
    {
        var item = new ToolStripMenuItem(label, null, (_, _) => SelectSkin(folderName))
        {
            Checked = folderName == activeSkinName
        };
        _skinMenu.DropDownItems.Add(item);
        _skinItems.Add((item, folderName));
    }

    private void SelectSkin(string? folderName)
    {
        foreach (var (item, itemFolderName) in _skinItems)
            item.Checked = itemFolderName == folderName;

        var settings = _settingsStore.Load() with { ActiveSkinName = folderName };
        _settingsStore.Save(settings);

        SkinSelected?.Invoke(folderName);
    }

    private void RefreshTooltip()
    {
        _notifyIcon.Text = TooltipFormatter.Format(_lastUsage, _lastRateLimit, _lastSubscriptionUsage);
    }

    private void ToggleDragMode(object? sender, EventArgs e)
    {
        _dragMode = !_dragMode;
        _dragItem.Checked = _dragMode;
        _petWindow.SetDragMode(_dragMode);
    }

    private void ToggleRunAtStartup(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        var newValue = !item.Checked;

        // StartupRegistration.SetEnabled touches the registry and can throw
        // (SecurityException / UnauthorizedAccessException) if the process lacks
        // registry access. Since this runs directly on a tray-menu click with no
        // global dispatcher-exception handler wired up yet, an uncaught throw here
        // would take down the whole app. Only flip the checkbox and persist the
        // setting once the registry write actually succeeds, so on failure the
        // menu state and saved settings stay consistent with reality.
        try
        {
            StartupRegistration.SetEnabled(newValue);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            _log.Write(
                $"[TrayIconManager.ToggleRunAtStartup] Failed to set startup registration to {newValue}: {ex}");
            return;
        }

        item.Checked = newValue;
        var settings = _settingsStore.Load() with { RunAtStartup = newValue };
        _settingsStore.Save(settings);
    }

    private void ToggleSubscriptionUsage(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        var newValue = !item.Checked;

        item.Checked = newValue;
        var settings = _settingsStore.Load() with { ShowSubscriptionUsage = newValue };
        _settingsStore.Save(settings);

        SubscriptionUsageToggled?.Invoke(newValue);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}

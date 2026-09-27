using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public sealed class TrayIconManager : IDisposable
{
    // Real DeviceIndex values are always 1-6 (see DeviceDiscoveryService), so 0 is a safe sentinel
    // for the "no devices found at all" placeholder icon below.
    private const byte StatusIconKey = 0;

    private readonly DeviceRegistry _registry;
    private readonly SettingsStore _settingsStore;
    private readonly Action _onIconClicked;
    private readonly Dictionary<byte, NotifyIcon> _icons = new();
    // Captured at construction time (App.OnStartup, i.e. the UI thread). DeviceRegistry.Updated
    // fires from PollingService.PollNow(), which runs on a System.Threading.Timer thread-pool
    // thread — a NotifyIcon created there never gets its message loop pumped, so its Click event
    // never fires and the main window becomes unreachable.
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    public TrayIconManager(DeviceRegistry registry, SettingsStore settingsStore, Action onIconClicked)
    {
        _registry = registry;
        _settingsStore = settingsStore;
        _onIconClicked = onIconClicked;
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _registry.Updated += Refresh;
        Refresh();
    }

    // Always runs on the UI thread (see _dispatcher). That also makes every _icons access
    // single-threaded, since ShowBalloonTip/Dispose are only ever called from the UI thread too.
    private void Refresh()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Refresh);
            return;
        }

        var settings = _settingsStore.Load();
        var currentTracked = _registry.Devices;

        if (currentTracked.Count == 0)
        {
            // No receiver found, or a receiver found but zero paired devices — with no per-device
            // icons at all, the app would otherwise be completely invisible and unreachable (no
            // way to even open the settings window). Keep exactly one placeholder icon up instead.
            RemoveStaleDeviceIcons(keep: new HashSet<byte>());
            ShowStatusIcon();
            return;
        }

        RemoveStatusIcon(); // devices are back — drop the placeholder

        var currentIndices = new HashSet<byte>(currentTracked.Select(d => d.Info.DeviceIndex));
        RemoveStaleDeviceIcons(currentIndices);

        foreach (TrackedDevice tracked in currentTracked)
        {
            LogiBoltDeviceInfo device = tracked.Info;
            bool visible = !settings.DeviceVisibility.TryGetValue(device.Name, out bool hidden) || !hidden;
            // DeviceVisibility maps name -> hidden=true means "don't show"; absent key defaults to visible.

            if (!_icons.TryGetValue(device.DeviceIndex, out var icon))
            {
                icon = new NotifyIcon { Visible = false };
                icon.Click += (_, _) => _onIconClicked();
                _icons[device.DeviceIndex] = icon;
            }

            // Icon.FromHandle (used by TrayBitmapRenderer) wraps a raw GDI handle that .NET does
            // NOT free on Icon.Dispose() — without DestroyIcon this leaks one GDI handle per
            // redraw and eventually exhausts the process's GDI handle quota on a long-running
            // tray app that redraws every poll cycle.
            Icon? previousIcon = icon.Icon;
            icon.Icon = TrayBitmapRenderer.Render(device, settings.IconStyle);
            DisposeRenderedIcon(previousIcon);

            string status = device.Battery.IsCharging ? "заряжается" : "не заряжается";
            string staleSuffix = tracked.IsStale ? " — устарело" : "";
            icon.Text = Truncate($"{device.Name}: {device.Battery.Percent}% ({status}){staleSuffix}", 63); // NotifyIcon.Text max length
            icon.Visible = visible;
        }
    }

    private void RemoveStaleDeviceIcons(HashSet<byte> keep)
    {
        foreach (byte staleIndex in _icons.Keys.Where(k => k != StatusIconKey).Except(keep).ToList())
        {
            // Order matters: hide first so the shell drops its reference via NIM_DELETE, and only
            // then release the underlying GDI icon handle. (Same ordering as the redraw path above,
            // which assigns the new icon before destroying the old one.)
            NotifyIcon staleIcon = _icons[staleIndex];
            staleIcon.Visible = false;          // Shell_NotifyIcon(NIM_DELETE)
            DisposeRenderedIcon(staleIcon.Icon); // now safe to free the GDI handle
            staleIcon.Dispose();
            _icons.Remove(staleIndex);
        }
    }

    private void ShowStatusIcon()
    {
        if (!_icons.TryGetValue(StatusIconKey, out var icon))
        {
            icon = new NotifyIcon { Visible = false };
            icon.Click += (_, _) => _onIconClicked();
            _icons[StatusIconKey] = icon;
        }

        // SystemIcons.Warning is a shared, framework-owned Icon — unlike TrayBitmapRenderer's
        // Icon.FromHandle output, it must NEVER be passed to DisposeRenderedIcon/DestroyIcon.
        icon.Icon ??= SystemIcons.Warning;
        icon.Text = "LogiBoltTray: устройства не найдены. Клик — открыть окно, подробности — %AppData%\\LogiBoltTray\\diagnostics.log";
        icon.Visible = true;
    }

    private void RemoveStatusIcon()
    {
        if (!_icons.TryGetValue(StatusIconKey, out var icon))
        {
            return;
        }

        icon.Visible = false; // SystemIcons.Warning is shared — dispose the NotifyIcon only, never DestroyIcon it
        icon.Dispose();
        _icons.Remove(StatusIconKey);
    }

    public void ShowBalloonTip(LogiBoltDeviceInfo device)
    {
        if (_icons.TryGetValue(device.DeviceIndex, out var icon))
        {
            icon.BalloonTipTitle = "Низкий заряд";
            icon.BalloonTipText = $"{device.Name}: {device.Battery.Percent}%";
            icon.ShowBalloonTip(5000);
        }
    }

    private static void DisposeRenderedIcon(Icon? icon)
    {
        if (icon is null)
        {
            return;
        }

        NativeMethods.DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    public void Dispose()
    {
        _registry.Updated -= Refresh;
        foreach (var entry in _icons)
        {
            // Same ordering as the stale-removal loop: NIM_DELETE first, then free the GDI handle —
            // except the status icon, whose Icon is the shared SystemIcons.Warning and must never
            // be passed to DestroyIcon.
            entry.Value.Visible = false;
            if (entry.Key != StatusIconKey)
            {
                DisposeRenderedIcon(entry.Value.Icon);
            }
            entry.Value.Dispose();
        }
        _icons.Clear();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public sealed class DeviceRow
{
    public required string Name { get; init; }
    public required DeviceType Type { get; init; }
    public required string PercentText { get; init; }
    public required string StatusText { get; init; }
}

public partial class MainWindow : Window
{
    private readonly DeviceRegistry _registry;
    private readonly PollingService _pollingService;
    private readonly SettingsStore _settingsStore;
    private SettingsModel _settings = null!;
    private bool _isLoadingSettings;
    // Set only by ExitButton_Click, right before a real Application.Shutdown(). Distinguishes "the
    // user asked to fully quit" from every other way this window's Closing event can fire (the X
    // button, Alt+F4, CloseButton) — those should minimize to tray, not exit, per the design: tray
    // icons are the persistent interface, this window is secondary. Without this flag, cancelling
    // Closing during an actual Shutdown() can abort the shutdown itself instead of just this window.
    private bool _isExiting;

    public MainWindow(DeviceRegistry registry, PollingService pollingService, SettingsStore settingsStore)
    {
        InitializeComponent();
        _registry = registry;
        _pollingService = pollingService;
        _settingsStore = settingsStore;
        _registry.Updated += RefreshList;
        Closing += MainWindow_Closing;
        RefreshList();
        LoadSettingsIntoUi();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            return; // real exit in progress (ExitButton_Click) — let the close proceed
        }

        e.Cancel = true;
        Hide(); // tray icons stay the persistent interface; this window just hides
    }

    private void RefreshList()
    {
        Dispatcher.Invoke(() =>
        {
            DeviceListView.ItemsSource = _registry.Devices.Select(t => new DeviceRow
            {
                Name = t.Info.Name,
                Type = t.Info.Type,
                PercentText = $"{t.Info.Battery.Percent}%",
                StatusText = (t.Info.Battery.IsCharging ? "Заряжается" : "Подключено") + (t.IsStale ? " (устарело)" : ""),
            }).ToList();
            RebuildDeviceVisibilityPanel();
        });
    }

    private void LoadSettingsIntoUi()
    {
        _isLoadingSettings = true;
        _settings = _settingsStore.Load();

        PollIntervalTextBox.Text = _settings.PollIntervalSeconds.ToString();
        LowBatteryEnabledCheckBox.IsChecked = _settings.LowBatteryNotificationsEnabled;
        LowBatteryThresholdTextBox.Text = _settings.LowBatteryThresholdPercent.ToString();
        AutostartCheckBox.IsChecked = _settings.AutostartEnabled;

        BuildIconStylePanel();
        RebuildDeviceVisibilityPanel();
        _isLoadingSettings = false;
    }

    private void BuildIconStylePanel()
    {
        IconStylePanel.Children.Clear();

        IconStyle[] styles = Enum.GetValues<IconStyle>();
        // Sample device used purely to render a representative preview icon per style — 76% on a
        // mouse is an arbitrary mid-range choice with nothing charging, so all four styles show a
        // clearly non-edge-case color/shape.
        System.Drawing.Icon[] previewIcons = IconStylePreview.RenderAllStyles(samplePercent: 76, sampleType: DeviceType.Mouse);

        try
        {
            for (int i = 0; i < styles.Length; i++)
            {
                IconStyle style = styles[i];
                BitmapSource previewSource = Imaging.CreateBitmapSourceFromHIcon(
                    previewIcons[i].Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                var content = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                content.Children.Add(new System.Windows.Controls.Image
                {
                    Source = previewSource,
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 2),
                });
                content.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = style.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 10,
                });

                var radio = new System.Windows.Controls.RadioButton
                {
                    Content = content,
                    GroupName = "IconStyle",
                    IsChecked = style == _settings.IconStyle,
                    Tag = style,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                radio.Checked += IconStyleRadio_Checked;
                IconStylePanel.Children.Add(radio);
            }
        }
        finally
        {
            // Imaging.CreateBitmapSourceFromHIcon copies pixel data into a managed BitmapSource, so
            // the raw HICONs from IconStylePreview are no longer needed after the loop above — release
            // them immediately (same DestroyIcon pattern as TrayIconManager, see Task 16/17) rather than
            // leaking one GDI handle per style every time this panel rebuilds.
            foreach (System.Drawing.Icon icon in previewIcons)
            {
                NativeMethods.DestroyIcon(icon.Handle);
                icon.Dispose();
            }
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }

    private void RebuildDeviceVisibilityPanel()
    {
        // Guard against the constructor's ordering: RefreshList() (which now also calls this
        // method) runs before LoadSettingsIntoUi() populates _settings, so on that very first
        // call _settings is still null. If devices were already discovered by the time this
        // window opens (PollingService.Start() polls once at app startup, before any window
        // exists), _registry.Devices is non-empty here and the loop below would otherwise
        // dereference a null _settings. LoadSettingsIntoUi() calls this method again right after
        // settings load, so skipping this first pass is harmless — the panel gets built correctly
        // moments later with real settings data.
        if (_settings is null) return;

        DeviceVisibilityPanel.Children.Clear();
        foreach (var tracked in _registry.Devices)
        {
            string deviceName = tracked.Info.Name;
            bool hidden = _settings.DeviceVisibility.TryGetValue(deviceName, out bool h) && h;
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content = deviceName,
                IsChecked = !hidden,
                Tag = deviceName,
            };
            checkBox.Checked += DeviceVisibilityCheckBox_Changed;
            checkBox.Unchecked += DeviceVisibilityCheckBox_Changed;
            DeviceVisibilityPanel.Children.Add(checkBox);
        }
    }

    private void PollIntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        if (!int.TryParse(PollIntervalTextBox.Text, out int requested)) return;

        int clamped = PollIntervalPolicy.Clamp(requested);
        PollIntervalTextBox.Text = clamped.ToString();
        _settings.PollIntervalSeconds = clamped;
        _settingsStore.Save(_settings);
        _pollingService.Restart();
    }

    private void IconStyleRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        var radio = (System.Windows.Controls.RadioButton)sender;
        _settings.IconStyle = (IconStyle)radio.Tag;
        _settingsStore.Save(_settings);
        // Redraw from the already-known device list under the new settings. This used to call
        // PollNow(), which re-polled real hardware (several seconds when paired devices are absent
        // and each dead index burns three ~500ms timeouts) and froze the window for a purely
        // cosmetic change.
        _registry.NotifyUpdated();
    }

    private void DeviceVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        var checkBox = (System.Windows.Controls.CheckBox)sender;
        string deviceName = (string)checkBox.Tag;
        _settings.DeviceVisibility[deviceName] = checkBox.IsChecked != true;
        _settingsStore.Save(_settings);
        _registry.NotifyUpdated(); // cosmetic-only change — no hardware poll needed (see above)
    }

    private void LowBatteryEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        _settings.LowBatteryNotificationsEnabled = LowBatteryEnabledCheckBox.IsChecked == true;
        _settingsStore.Save(_settings);
    }

    private void LowBatteryThresholdTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        if (!int.TryParse(LowBatteryThresholdTextBox.Text, out int requested)) return;

        int clamped = Math.Clamp(requested, 1, 99);
        LowBatteryThresholdTextBox.Text = clamped.ToString();
        _settings.LowBatteryThresholdPercent = clamped;
        _settingsStore.Save(_settings);
    }

    private void AutostartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        bool enabled = AutostartCheckBox.IsChecked == true;
        AutostartManager.SetEnabled(enabled);
        _settings.AutostartEnabled = enabled;
        _settingsStore.Save(_settings);
    }

    // A real hardware poll can take several seconds, so it must not run on the UI thread.
    // PollingService.PollNow() has its own reentrancy guard and swallows its own exceptions, and
    // both TrayIconManager.Refresh and RefreshList marshal back to the dispatcher, so firing it
    // onto the thread pool is safe.
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Task.Run(() => _pollingService.PollNow());

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close(); // -> MainWindow_Closing -> Hide()

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _registry.Updated -= RefreshList;
        base.OnClosed(e);
    }
}

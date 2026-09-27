using System;
using System.IO;
using System.Windows;
using LogiBoltTray.Core;
using LogiBoltTray.Hid;

namespace LogiBoltTray.App;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private DeviceRegistry? _deviceRegistry;
    private PollingService? _pollingService;
    private SettingsStore? _settingsStore;
    private TrayIconManager? _trayIconManager;
    private MainWindow? _mainWindow;

    // %AppData%\LogiBoltTray — shared by the settings file and the diagnostics log.
    private static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogiBoltTray");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Run `LogiBoltTray.exe --diagnose` from a terminal to dump every Logitech HID interface
        // Windows can see (VID/PID/UsagePage/Usage/Interface) without starting the tray app at
        // all — this is how to find the real values if BoltReceiverIds' defaults (PID 0xC548,
        // UsagePage 0xFF43) don't match the user's actual receiver.
        if (Array.Exists(e.Args, a => a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase)))
        {
            RunDiagnosticAndExit();
            return;
        }

        // Wired first so that a failure anywhere in the rest of startup is still recorded.
        // The lambda parameter is named `args` rather than `e` because `e` is already taken by
        // this method's StartupEventArgs parameter (C# forbids shadowing it inside a lambda).
        DispatcherUnhandledException += (_, args) => LogCrash(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);

        base.OnStartup(e);

        _instanceGuard = new SingleInstanceGuard("LogiBoltTray-SingleInstance");
        if (!_instanceGuard.IsFirstInstance)
        {
            MessageBox.Show("LogiBoltTray is already running — check the system tray.", "LogiBoltTray");
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown; // no window open yet, don't exit immediately

        _settingsStore = new SettingsStore(AppDataDirectory);
        _deviceRegistry = new DeviceRegistry();
        _pollingService = new PollingService(_deviceRegistry, _settingsStore);
        // Poll status/failures land in diagnostics.log — the poll cycle otherwise fails silently
        // (see PollingService.PollCore's catch-all), which would leave "0 devices, no tray icon"
        // completely unexplained.
        _pollingService.Diagnostic += LogDiagnosticLine;
        _pollingService.Start();

        _trayIconManager = new TrayIconManager(_deviceRegistry!, _settingsStore!, onIconClicked: ShowMainWindow);

        _pollingService!.LowBatteryDetected += device =>
        {
            Dispatcher.Invoke(() => _trayIconManager?.ShowBalloonTip(device));
        };
    }

    private void RunDiagnosticAndExit()
    {
        string output;
        try
        {
            output = DiagnosticDump.DumpAllLogitechInterfaces();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = "(no Logitech HID interfaces found at all — check the receiver is plugged in "
                    + "and shows up under a Logitech-branded entry in Windows Device Manager)";
            }
        }
        catch (Exception ex)
        {
            output = $"DiagnosticDump threw: {ex}";
        }

        string path = Path.Combine(AppDataDirectory, "diagnostic.txt");
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            File.WriteAllText(path, output);
        }
        catch
        {
            // Fall through to the MessageBox even if the file write itself failed.
        }

        MessageBox.Show($"{output}\n\nAlso written to:\n{path}", "LogiBoltTray diagnostics");
        Shutdown();
    }

    // Deliberately does NOT set e.Handled on the dispatcher exception: the app still crashes, but
    // now there is a record of why. Staying alive in a half-initialised state would only produce a
    // second, more confusing failure later.
    private static void LogCrash(Exception? exception)
    {
        if (exception is not null)
        {
            LogDiagnosticLine(exception.ToString());
        }
    }

    private static void LogDiagnosticLine(string message)
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            File.AppendAllText(
                Path.Combine(AppDataDirectory, "diagnostics.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // A diagnostics logger must never itself throw — there is nowhere left to report to.
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(_deviceRegistry!, _pollingService!, _settingsStore!);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        _pollingService?.Dispose();

        try
        {
            // hidapi asks callers to release its global state on shutdown. Done after the polling
            // service is disposed so no poll is still using a device handle.
            HidApi.Hid.Exit();
        }
        catch
        {
            // A cleanup call must never throw and block shutdown.
        }

        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}

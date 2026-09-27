using LogiBoltTray.Core;
using LogiBoltTray.Hid;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public sealed class PollingService : IDisposable
{
    private readonly DeviceRegistry _registry;
    private readonly SettingsStore _settingsStore;
    // Constructed once — never replaced — so notified-state persists across polls.
    private readonly LowBatteryNotifier _lowBatteryNotifier;
    private System.Threading.Timer? _timer;
    // 0 = idle, 1 = a poll is running. PollNow can now be triggered from three places at once
    // (timer tick, startup Task.Run, manual Refresh button) and two of them must never open the
    // same HID device simultaneously.
    private int _pollInProgress;
    // Tracks the last reported status/error so Diagnostic only fires on a change, not every
    // cycle forever — otherwise a permanently-empty device list would grow the log unbounded.
    private string? _lastStatus;
    private string? _lastError;

    public event Action<LogiBoltDeviceInfo>? LowBatteryDetected;
    // Fires on every poll-cycle status change (found/lost the receiver, device count changed) and
    // on every distinct failure — the only way to tell "zero devices" (a working poll that
    // legitimately found nothing) apart from "the poll is silently failing every cycle", since
    // PollCore's catch-all below must never let an exception escape to the timer thread.
    public event Action<string>? Diagnostic;

    public PollingService(DeviceRegistry registry, SettingsStore settingsStore)
    {
        _registry = registry;
        _settingsStore = settingsStore;
        _lowBatteryNotifier = new LowBatteryNotifier(settingsStore.Load().LowBatteryThresholdPercent);
    }

    public void Start()
    {
        int intervalSeconds = PollIntervalPolicy.Clamp(_settingsStore.Load().PollIntervalSeconds);
        // Start() is called from the UI thread (App.OnStartup, and MainWindow via Restart()), and a
        // poll can take several seconds when paired devices are absent — run the initial one on the
        // thread pool so startup and the settings UI stay responsive.
        Task.Run(PollNow);
        _timer = new System.Threading.Timer(_ => PollNow(), null, TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(intervalSeconds));
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // v1 supports exactly one physical Bolt receiver at a time; if multiple are connected, only
    // whichever one ReceiverEnumerator.FindShortInterface() happens to return is polled. This is a
    // deliberate scope decision — DeviceRegistry keys devices by plain byte DeviceIndex (1-6),
    // which is only unique within a single receiver, so polling more than one receiver risks a
    // DeviceIndex collision across receivers that would throw from ToDictionary on the periodic
    // timer thread and crash the process.
    public void PollNow()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _pollInProgress, 1, 0) != 0)
        {
            return; // a poll is already running; skip this trigger rather than overlap on the HID device
        }

        try
        {
            PollCore();
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _pollInProgress, 0);
        }
    }

    private void PollCore()
    {
        // Nothing may escape this method: it runs as a System.Threading.Timer callback (and on
        // thread-pool threads), and an exception escaping a timer callback terminates the entire
        // .NET process — an unobserved Task faults silently instead, which is no better. For a
        // tray-only app with no window open by default that shows up as every icon silently
        // vanishing with no explanation. Swallow-and-skip-this-cycle is the right behaviour for a
        // periodic poll — the next tick tries again. (Unguarded sources include
        // ReceiverEnumerator.FindShortInterface()/FindLongInterface() and _settingsStore.Load(),
        // which can throw IOException on a sharing violation if MainWindow saves settings
        // concurrently.)
        try
        {
            var settings = _settingsStore.Load();
            _lowBatteryNotifier.UpdateThreshold(settings.LowBatteryThresholdPercent);

            var allDevices = new List<LogiBoltDeviceInfo>();

            // The short collection is required (every request this app sends is a short report);
            // the long collection is used opportunistically for full device names — see
            // BoltReceiverIds/HidppHidTransport for why Windows exposes these as two separate
            // HID device paths on real Bolt receiver hardware.
            var shortInterface = ReceiverEnumerator.FindShortInterface();
            if (shortInterface is not null)
            {
                try
                {
                    using var shortDevice = shortInterface.ConnectToDevice();
                    using var longDevice = ReceiverEnumerator.FindLongInterface()?.ConnectToDevice();
                    using var transport = new HidppHidTransport(shortDevice, longDevice);
                    var discovered = new DeviceDiscoveryService(transport).DiscoverDevices(out var batteryDiagnostics);
                    allDevices.AddRange(discovered);
                    // Raw per-device battery reads, unconditional (not deduped like the status
                    // line below) — this is what actually explains a wrong/unstable percentage,
                    // which a same-device-count status line can't distinguish from a correct one.
                    foreach (string line in batteryDiagnostics)
                    {
                        Diagnostic?.Invoke(line);
                    }
                }
                catch (HidApi.HidException)
                {
                    // Receiver interface disappeared between enumeration and connect (unplugged) —
                    // report the devices we do have (i.e. none) rather than abandoning the cycle,
                    // so DeviceRegistry still ages out the now-absent devices.
                }
            }

            _registry.Replace(allDevices);
            _lastError = null; // this cycle completed without throwing — clear any prior error latch

            string status = shortInterface is null
                ? "no Bolt receiver found (run \"LogiBoltTray.exe --diagnose\" to see every Logitech HID interface Windows reports, and compare against BoltReceiverIds' expected PID/UsagePage/Usage)"
                : $"receiver found, {allDevices.Count} device(s) discovered";
            if (status != _lastStatus)
            {
                _lastStatus = status;
                Diagnostic?.Invoke($"poll: {status}");
            }

            if (settings.LowBatteryNotificationsEnabled)
            {
                foreach (var device in allDevices)
                {
                    if (_lowBatteryNotifier.ShouldNotify(device.Name, device.Battery.Percent))
                    {
                        LowBatteryDetected?.Invoke(device);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Skip this cycle entirely rather than taking the process down (see comment above),
            // but only log a given failure once — if the same exception recurs every cycle this
            // keeps the log from growing without bound.
            string message = ex.ToString();
            if (message != _lastError)
            {
                _lastError = message;
                Diagnostic?.Invoke($"poll failed: {message}");
            }
        }
    }

    public void Dispose() => Stop();
}

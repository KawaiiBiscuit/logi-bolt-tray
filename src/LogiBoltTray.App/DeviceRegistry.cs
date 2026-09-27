using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

// Requires ImplicitUsings (enabled by the WPF project template) for System.Linq's
// ToDictionary/Select extension methods used below.
public readonly record struct TrackedDevice(LogiBoltDeviceInfo Info, bool IsStale);

public sealed class DeviceRegistry
{
    private readonly object _lock = new();
    private IReadOnlyList<TrackedDevice> _devices = Array.Empty<TrackedDevice>();

    public event Action? Updated;

    public IReadOnlyList<TrackedDevice> Devices
    {
        get { lock (_lock) { return _devices; } }
    }

    /// <summary>
    /// Re-raises <see cref="Updated"/> without touching hardware, so purely cosmetic setting
    /// changes (icon style, per-device visibility) can force a redraw from the already-known
    /// device list instead of paying for a full multi-second hardware poll.
    /// </summary>
    public void NotifyUpdated() => Updated?.Invoke();

    public void Replace(IReadOnlyList<LogiBoltDeviceInfo> freshScan)
    {
        lock (_lock)
        {
            var freshByIndex = freshScan.ToDictionary(d => d.DeviceIndex);
            var merged = new List<TrackedDevice>();

            foreach (var previous in _devices)
            {
                if (freshByIndex.TryGetValue(previous.Info.DeviceIndex, out var fresh))
                {
                    merged.Add(new TrackedDevice(fresh, IsStale: false));
                    freshByIndex.Remove(previous.Info.DeviceIndex);
                }
                else if (!previous.IsStale)
                {
                    merged.Add(previous with { IsStale = true }); // one grace cycle before dropping
                }
                // else: already stale and still missing this cycle -> drop (device unplugged/unpaired)
            }

            foreach (var newlySeen in freshByIndex.Values)
            {
                merged.Add(new TrackedDevice(newlySeen, IsStale: false));
            }

            _devices = merged;
        }

        Updated?.Invoke();
    }
}

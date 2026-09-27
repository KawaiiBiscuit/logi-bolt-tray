namespace LogiBoltTray.Protocol;

public sealed class DeviceDiscoveryService
{
    private const byte FirstDeviceIndex = 0x01;
    private const byte LastDeviceIndex = 0x06; // Bolt receivers support up to 6 paired devices

    private readonly IHidppTransport _transport;

    public DeviceDiscoveryService(IHidppTransport transport)
    {
        _transport = transport;
    }

    public IReadOnlyList<LogiBoltDeviceInfo> DiscoverDevices() => DiscoverDevices(out _);

    /// <summary>
    /// Same as <see cref="DiscoverDevices()"/>, but also reports which battery feature answered
    /// (and the raw response bytes) for every responding index — for field diagnosis only, see
    /// <see cref="BatteryReader.LastDiagnostics"/>.
    /// </summary>
    public IReadOnlyList<LogiBoltDeviceInfo> DiscoverDevices(out List<string> batteryDiagnostics)
    {
        var devices = new List<LogiBoltDeviceInfo>();
        batteryDiagnostics = new List<string>();

        for (byte deviceIndex = FirstDeviceIndex; deviceIndex <= LastDeviceIndex; deviceIndex++)
        {
            var batteryReader = new BatteryReader(_transport, deviceIndex);
            var battery = batteryReader.Read();
            if (batteryReader.LastDiagnostics is { } diagnostic)
            {
                batteryDiagnostics.Add(diagnostic);
            }

            if (battery.IsUnknown)
            {
                continue; // device not paired at this index, or doesn't support any known battery feature
            }

            var nameClient = new DeviceNameClient(_transport, deviceIndex);
            string name = nameClient.GetName() ?? $"Device {deviceIndex}";
            DeviceType type = nameClient.GetDeviceType();
            // The kind-byte -> DeviceType mapping in DeviceNameClient was an unverified guess at
            // design time (no real hardware access) — log the raw byte so it can be corrected
            // against whatever real devices actually report, instead of guessing again.
            batteryDiagnostics.Add($"dev{deviceIndex} name=\"{name}\" kindByte={nameClient.LastKindByte?.ToString("X2") ?? "n/a"} -> type={type}");

            devices.Add(new LogiBoltDeviceInfo(deviceIndex, name, type, battery));
        }

        return devices;
    }
}

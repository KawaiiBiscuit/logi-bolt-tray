namespace LogiBoltTray.Protocol;

public sealed class BatteryReader
{
    private const ushort BatteryStatusFeatureId = 0x1000;
    private const ushort UnifiedBatteryFeatureId = 0x1004;
    private const ushort BatteryVoltageFeatureId = 0x1001;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidppTransport _transport;
    private readonly byte _deviceIndex;
    private readonly RootFeatureClient _rootFeatureClient;

    public BatteryReader(IHidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        _deviceIndex = deviceIndex;
        _rootFeatureClient = new RootFeatureClient(transport, deviceIndex);
    }

    /// <summary>
    /// Set by the most recent <see cref="Read"/> call: which feature/response produced the
    /// result (or why none did). Not part of the tested return value on purpose — this exists
    /// purely so callers can log it for field diagnosis (see PollingService.Diagnostic) without
    /// changing BatteryStatus's shape.
    /// </summary>
    public string? LastDiagnostics { get; private set; }

    public BatteryStatus Read()
    {
        byte? legacyIndex = _rootFeatureClient.FindFeatureIndex(BatteryStatusFeatureId);
        if (legacyIndex is { } legacy)
        {
            var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, legacy, 0x00, SoftwareIdSequence.Next()), DefaultTimeout);
            if (response is { IsError: false } r)
            {
                int percent = r.Params[0];
                bool charging = r.Params[2] != 0x00 && r.Params[2] != 0x03; // 0=discharging, 3=full are "not charging"
                LastDiagnostics = $"dev{_deviceIndex} via 0x1000@{legacy:X2} {FormatFrame(r)} -> {percent}% charging={charging}";
                return new BatteryStatus(percent, charging, IsUnknown: false);
            }
        }

        byte? unifiedIndex = _rootFeatureClient.FindFeatureIndex(UnifiedBatteryFeatureId);
        if (unifiedIndex is { } unified)
        {
            var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, unified, 0x01, SoftwareIdSequence.Next()), DefaultTimeout);
            if (response is { IsError: false } r)
            {
                int percent = r.Params[0];
                bool charging = r.Params[2] != 0x00;
                LastDiagnostics = $"dev{_deviceIndex} via 0x1004@{unified:X2} {FormatFrame(r)} -> {percent}% charging={charging}";
                return new BatteryStatus(percent, charging, IsUnknown: false);
            }
        }

        byte? voltageIndex = _rootFeatureClient.FindFeatureIndex(BatteryVoltageFeatureId);
        if (voltageIndex is { } voltage)
        {
            var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, voltage, 0x00, SoftwareIdSequence.Next()), DefaultTimeout);
            if (response is { IsError: false } r)
            {
                int millivolts = (r.Params[0] << 8) | r.Params[1];
                bool charging = (r.Params[2] & 0x01) != 0;
                int percent = BatteryVoltageTable.ToPercent(millivolts);
                LastDiagnostics = $"dev{_deviceIndex} via 0x1001@{voltage:X2} {FormatFrame(r)} mV={millivolts} -> {percent}% charging={charging}";
                return new BatteryStatus(percent, charging, IsUnknown: false);
            }
        }

        LastDiagnostics = $"dev{_deviceIndex}: no battery feature responded (0x1000={legacyIndex?.ToString("X2") ?? "not found"}, 0x1004={unifiedIndex?.ToString("X2") ?? "not found"}, 0x1001={voltageIndex?.ToString("X2") ?? "not found"})";
        return BatteryStatus.Unknown();
    }

    // Logs the full response, not just the first 3 bytes BatteryReader itself acts on — a Long
    // (0x11) response carries 16 param bytes, and if any feature ever returns more data than this
    // reader currently uses (e.g. a device-computed percentage estimate alongside raw voltage),
    // this is what would reveal it instead of silently discarding it.
    private static string FormatFrame(HidppFrame r) => $"reportId=0x{r.ReportId:X2} raw=[{string.Join(",", r.Params)}]";
}

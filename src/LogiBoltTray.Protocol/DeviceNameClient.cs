using System.Text;

namespace LogiBoltTray.Protocol;

public sealed class DeviceNameClient
{
    private const ushort DeviceNameFeatureId = 0x0005;
    private const byte GetLengthFunctionId = 0x00;
    private const byte GetNameFunctionId = 0x01;
    private const byte GetKindFunctionId = 0x02;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidppTransport _transport;
    private readonly byte _deviceIndex;
    private readonly RootFeatureClient _rootFeatureClient;
    private byte? _cachedFeatureIndex;

    /// <summary>
    /// Set by <see cref="GetDeviceType"/>: the raw "kind" byte the device reported, before it's
    /// mapped to a <see cref="DeviceType"/>. The mapping in GetDeviceType was an unverified guess
    /// at design time — this exists purely so a caller can log the raw byte for field diagnosis
    /// (see PollingService.Diagnostic) and correct the mapping against real hardware.
    /// </summary>
    public byte? LastKindByte { get; private set; }

    public DeviceNameClient(IHidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        _deviceIndex = deviceIndex;
        _rootFeatureClient = new RootFeatureClient(transport, deviceIndex);
    }

    // GetName() and GetDeviceType() both address feature 0x0005, so without this the root-feature
    // round-trip (and, for an absent device, its full timeout) was paid twice per device per poll.
    // A miss deliberately stays uncached and retries on the next call: a DeviceNameClient is
    // constructed fresh per device per poll cycle, so there is no cross-poll staleness risk.
    private byte? ResolveFeatureIndex()
    {
        _cachedFeatureIndex ??= _rootFeatureClient.FindFeatureIndex(DeviceNameFeatureId);
        return _cachedFeatureIndex;
    }

    public string? GetName()
    {
        byte? featureIndex = ResolveFeatureIndex();
        if (featureIndex is not { } index)
        {
            return null;
        }

        var lengthResponse = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, index, GetLengthFunctionId, SoftwareIdSequence.Next()), DefaultTimeout);
        if (lengthResponse is not { IsError: false } lr)
        {
            return null;
        }

        int length = lr.Params[0];
        var nameResponse = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, index, GetNameFunctionId, SoftwareIdSequence.Next()), DefaultTimeout);
        if (nameResponse is not { IsError: false } nr)
        {
            return null;
        }

        int available = Math.Min(length, nr.Params.Length);
        return Encoding.ASCII.GetString(nr.Params, 0, available);
    }

    public DeviceType GetDeviceType()
    {
        byte? featureIndex = ResolveFeatureIndex();
        if (featureIndex is not { } index)
        {
            return DeviceType.Unknown;
        }

        var response = _transport.SendAndReceive(HidppFrame.Short(_deviceIndex, index, GetKindFunctionId, SoftwareIdSequence.Next()), DefaultTimeout);
        if (response is not { IsError: false } r)
        {
            return DeviceType.Unknown;
        }

        LastKindByte = r.Params[0];
        return r.Params[0] switch
        {
            // 0x00 (Keyboard) and 0x03 (Mouse) confirmed against real hardware (MX Keys S, MX
            // Master 3S) on 2026-09-27 — the original 0x04/0x06/0x08 guesses were wrong (the real
            // mouse reported 0x03, which fell through to Other). Trackball/Headset below are still
            // unconfirmed guesses; correct them the same way once real hardware reports them.
            0x00 => DeviceType.Keyboard,
            0x03 => DeviceType.Mouse,
            0x05 => DeviceType.Trackball,
            0x08 => DeviceType.Headset,
            _ => DeviceType.Other,
        };
    }
}

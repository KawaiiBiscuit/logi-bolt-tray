namespace LogiBoltTray.Protocol;

public interface IHidppTransport
{
    /// <summary>
    /// Sends <paramref name="request"/> and waits up to <paramref name="timeout"/> for the
    /// matching response (same DeviceIndex/FeatureIndex/FunctionId/SoftwareId, or an error frame
    /// addressed to the same FeatureIndex/FunctionId/SoftwareId). Unrelated notification frames
    /// received in the meantime are discarded. Returns null on timeout.
    /// </summary>
    HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout);
}

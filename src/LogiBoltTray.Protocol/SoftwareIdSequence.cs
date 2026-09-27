namespace LogiBoltTray.Protocol;

/// <summary>
/// HID++ 2.0's SoftwareId (the low 4 bits of a request's function/softwareId byte) exists
/// specifically so a client can tell its own requests apart from each other — a device echoes it
/// back unchanged in its response. Every client in this codebase used to hardcode the same
/// constant (0x1) for every request, which defeats that purpose entirely: two DIFFERENT root
/// feature lookups (e.g. "does this device support 0x1000?" then "does it support 0x1004?") are
/// otherwise indistinguishable at the header level — same DeviceIndex, same root FeatureIndex
/// 0x00, same FunctionId 0x00, same SoftwareId. If the response to the FIRST lookup arrives a
/// little late (after this codebase already timed out on it and moved on to the SECOND lookup),
/// HidppFrame.Matches has no way to reject it, and the stale answer to "is 0x1000 supported" gets
/// silently accepted as the answer to "is 0x1004 supported" instead — confirmed on real hardware
/// (2026-09-27): a keyboard's genuine, correct battery reading (100%, charging, via feature 0x1004)
/// was replaced by a stale, unrelated response almost every poll cycle, producing a wrong, stuck
/// 57% reading via feature 0x1001 instead.
/// </summary>
internal static class SoftwareIdSequence
{
    // 0 is reserved by the HID++ 2.0 spec to mean "no response expected/wanted" — valid values for
    // a request that wants a reply are 1-15 (SoftwareId is a 4-bit field).
    private const byte MinValue = 1;
    private const byte MaxValue = 15;
    private static byte _next = MinValue;

    /// <summary>
    /// Returns a fresh value on every call, cycling 1-15. Not a global collision guarantee (only 15
    /// values exist), but more than enough to disambiguate the handful of requests a single device
    /// poll issues in quick succession — which is the actual failure mode this exists to prevent.
    /// </summary>
    public static byte Next()
    {
        byte value = _next;
        _next = _next >= MaxValue ? MinValue : (byte)(_next + 1);
        return value;
    }
}

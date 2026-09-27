using HidApi;
using HidApiClass = HidApi.Hid;

namespace LogiBoltTray.Hid;

public static class ReceiverEnumerator
{
    /// <summary>
    /// The collection that carries 7-byte HID++ short reports (report ID 0x10) — every request
    /// this app sends goes here. Null means no known Bolt receiver PID with this collection was
    /// found at all (receiver unplugged, or a PID/usage layout BoltReceiverIds doesn't know about
    /// yet — run DiagnosticDump.DumpAllLogitechInterfaces() to check).
    /// </summary>
    public static DeviceInfo? FindShortInterface() => Find(BoltReceiverIds.HidppShortUsage);

    /// <summary>
    /// The collection that carries 20-byte HID++ long reports (report ID 0x11) — e.g. device
    /// names. Null means only the short collection is usable; callers should still function with
    /// short-only communication (battery reading works fine), just without real device names.
    /// </summary>
    public static DeviceInfo? FindLongInterface() => Find(BoltReceiverIds.HidppLongUsage);

    private static DeviceInfo? Find(ushort usage)
    {
        foreach (ushort productId in BoltReceiverIds.KnownProductIds)
        {
            foreach (var info in HidApiClass.Enumerate(BoltReceiverIds.LogitechVendorId, productId))
            {
                if (info.UsagePage == BoltReceiverIds.HidppUsagePage && info.Usage == usage)
                {
                    return info;
                }
            }
        }

        return null;
    }
}

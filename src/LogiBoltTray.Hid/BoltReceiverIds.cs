namespace LogiBoltTray.Hid;

public static class BoltReceiverIds
{
    public const ushort LogitechVendorId = 0x046D;

    /// <summary>
    /// Most commonly documented Logitech Bolt receiver product ID. Bolt receivers may ship
    /// under additional PIDs — run DiagnosticDump.DumpAllLogitechInterfaces() on the target
    /// PC and add any missing PID found there.
    /// </summary>
    public static readonly ushort[] KnownProductIds = { 0xC548 };

    /// <summary>
    /// Vendor-defined HID usage page Logitech uses for the HID++ collections.
    /// Confirmed against real hardware (PID 0xC548) on 2026-09-15 — see ReceiverEnumerator.
    /// </summary>
    public const ushort HidppUsagePage = 0xFF00;

    /// <summary>
    /// Usage value of the top-level collection that carries 7-byte HID++ "short" reports
    /// (report ID 0x10). Every request this app sends is a short report, so this collection is
    /// always opened for writing.
    /// </summary>
    public const ushort HidppShortUsage = 0x0001;

    /// <summary>
    /// Usage value of the top-level collection that carries 20-byte HID++ "long" reports
    /// (report ID 0x11) — e.g. GetDeviceName responses. Confirmed on real hardware that Windows
    /// exposes this as a SEPARATE HID device path from the short collection above, even though
    /// both live on the same physical USB interface: hidapi opens by path (i.e. by collection),
    /// so a handle to one collection cannot read reports declared only in the other. Both must
    /// be opened and their reads merged — see HidppHidTransport.
    /// </summary>
    public const ushort HidppLongUsage = 0x0002;
}

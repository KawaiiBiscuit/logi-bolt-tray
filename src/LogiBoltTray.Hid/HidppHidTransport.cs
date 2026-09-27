using HidApi;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.Hid;

public sealed class HidppHidTransport : IHidppTransport, IDisposable
{
    // Real hardware trace (2026-09-15, Bolt receiver PID 0xC548) confirmed Windows exposes HID++
    // short (report ID 0x10) and long (report ID 0x11) reports as two SEPARATE top-level HID
    // collections, each its own device path, even though both live on the same physical USB
    // interface. hidapi opens by path — a handle to one collection cannot read reports declared
    // only in the other — so both are held here and every read polls both, merging whichever
    // arrives first. Every request this app sends is a short report, so writes always go to
    // _shortDevice; _longDevice may be null (e.g. on a receiver variant without a separate long
    // collection), in which case only short-report responses/notifications are ever seen.
    private readonly Device _shortDevice;
    private readonly Device? _longDevice;

    public HidppHidTransport(Device shortDevice, Device? longDevice)
    {
        _shortDevice = shortDevice;
        _longDevice = longDevice;
    }

    public HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout)
    {
        DrainStaleResponses();
        _shortDevice.Write(request.ToBytes());

        var deadline = DateTime.UtcNow + timeout;
        // Always allocate the max report size — responses aren't required to mirror the request's
        // report format (e.g. GetDeviceName replies as a 20-byte Long report even though the
        // request is a 7-byte Short one). HidppFrame.Parse(buffer.AsSpan(0, read)) already handles
        // a shorter actual read.
        var buffer = new byte[20];

        while (DateTime.UtcNow < deadline)
        {
            int remainingMs = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            // Interleave short bursts across both collections rather than blocking the whole
            // remaining budget on one — the response could land on either.
            int sliceMs = Math.Min(remainingMs, 25);

            var response = TryReadMatch(_shortDevice, buffer, sliceMs, request)
                        ?? TryReadMatch(_longDevice, buffer, sliceMs, request);
            if (response is not null)
            {
                return response;
            }
            // Otherwise nothing arrived, or it was an unrelated notification — keep waiting.
        }

        return null;
    }

    private static HidppFrame? TryReadMatch(Device? device, byte[] buffer, int timeoutMs, HidppFrame request)
    {
        if (device is null)
        {
            return null;
        }

        int read = device.ReadTimeout(buffer, timeoutMs);
        if (read <= 0)
        {
            return null;
        }

        var response = HidppFrame.Parse(buffer.AsSpan(0, read));
        return HidppFrame.Matches(request, response) ? response : null;
    }

    /// <summary>
    /// Discards anything already sitting in each HID read queue before a new request goes out.
    /// A request that timed out leaves its late response queued; without this it would be dequeued
    /// by the *next* SendAndReceive and, given how repetitive HID++ addressing is, could well be
    /// accepted as that request's reply (see <see cref="HidppFrame.Matches"/>).
    /// </summary>
    private void DrainStaleResponses()
    {
        // hid_read_timeout with a 0ms timeout is the documented non-blocking form: it returns
        // immediately, yielding 0 when no report is pending. So this loop costs nothing on the
        // common (empty-queue) path.
        var drainBuffer = new byte[20];
        while (_shortDevice.ReadTimeout(drainBuffer, 0) > 0)
        {
            // Intentionally empty — the report is dropped.
        }

        while (_longDevice?.ReadTimeout(drainBuffer, 0) > 0)
        {
            // Intentionally empty — the report is dropped.
        }
    }

    public void Dispose()
    {
        _shortDevice.Dispose();
        _longDevice?.Dispose();
    }
}

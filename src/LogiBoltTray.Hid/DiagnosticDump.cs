using System.Text;
using HidApi;
using HidApiClass = HidApi.Hid;

namespace LogiBoltTray.Hid;

public static class DiagnosticDump
{
    /// <summary>
    /// Run this once on the target Windows PC (see Task 8's manual verification steps) to
    /// find the real product ID / usage page / interface number of the connected Bolt
    /// receiver if the defaults in BoltReceiverIds don't find it.
    /// </summary>
    public static string DumpAllLogitechInterfaces()
    {
        var sb = new StringBuilder();
        foreach (var info in HidApiClass.Enumerate(BoltReceiverIds.LogitechVendorId, 0))
        {
            sb.AppendLine($"PID=0x{info.ProductId:X4} Usage=0x{info.Usage:X4} UsagePage=0x{info.UsagePage:X4} " +
                          $"Interface={info.InterfaceNumber} Product=\"{info.ProductString}\" Path={info.Path}");
        }

        return sb.ToString();
    }
}

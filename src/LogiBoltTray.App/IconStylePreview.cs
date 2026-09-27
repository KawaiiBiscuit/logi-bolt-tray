using System.Drawing;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public static class IconStylePreview
{
    public static Icon[] RenderAllStyles(int samplePercent, DeviceType sampleType)
    {
        var sampleDevice = new LogiBoltDeviceInfo(
            DeviceIndex: 0x01,
            Name: "Preview",
            Type: sampleType,
            Battery: new BatteryStatus(samplePercent, IsCharging: false, IsUnknown: false));

        return Enum.GetValues<IconStyle>()
            .Select(style => TrayBitmapRenderer.Render(sampleDevice, style))
            .ToArray();
    }
}

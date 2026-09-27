using System.Drawing;
using LogiBoltTray.Core;
using LogiBoltTray.Protocol;

namespace LogiBoltTray.App;

public static class TrayBitmapRenderer
{
    private static Color ToColor(BatteryColor color) => color switch
    {
        BatteryColor.Red => Color.FromArgb(192, 57, 43),
        BatteryColor.Orange => Color.FromArgb(230, 126, 34),
        BatteryColor.Green => Color.FromArgb(46, 204, 113),
        BatteryColor.Purple => Color.FromArgb(142, 68, 173),
        _ => Color.Gray,
    };

    // A small "+" corner badge (the first attempt at a charging indicator) tested unreadable at
    // 16x16 — no room to fit both the percent-tier color and a legible extra glyph. Tinting the
    // whole icon a distinct blue instead needs no extra space and can't be missed.
    private static readonly Color ChargingColor = Color.FromArgb(52, 152, 219);

    public static Icon Render(LogiBoltDeviceInfo device, IconStyle style)
    {
        Color color = device.Battery.IsCharging
            ? ChargingColor
            : ToColor(BatteryColorPolicy.GetColor(device.Battery.Percent));
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);

        switch (style)
        {
            case IconStyle.A_Number:
                DrawNumberStyle(g, device.Battery.Percent, color);
                break;
            case IconStyle.B_GlyphBadge:
                DrawGlyphBadgeStyle(g, device.Type, color);
                break;
            case IconStyle.C_BatteryBar:
                DrawBatteryBarStyle(g, device.Battery.Percent, color);
                break;
        }

        nint hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static void DrawNumberStyle(Graphics g, int percent, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, 16, 16);
        using var font = new Font("Segoe UI", 7, System.Drawing.FontStyle.Bold);
        var textSize = g.MeasureString(percent.ToString(), font);
        g.DrawString(percent.ToString(), font, Brushes.Black, (16 - textSize.Width) / 2, (16 - textSize.Height) / 2);
    }

    // GDI+ (System.Drawing.Graphics) cannot render color-emoji glyphs at all — confirmed on real
    // hardware: the mouse emoji didn't render (blank) and the keyboard emoji rendered as
    // unreadable tofu. A plain letter via DrawString uses the exact same proven text-rendering
    // path as DrawNumberStyle (which does work), so it's used here instead of an emoji.
    private static void DrawGlyphBadgeStyle(Graphics g, DeviceType type, Color color)
    {
        using var brush = new SolidBrush(color);
        using var font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
        string letter = type switch
        {
            DeviceType.Mouse => "M",
            DeviceType.Keyboard => "K",
            DeviceType.Headset => "H",
            DeviceType.Trackball => "T",
            _ => "?",
        };
        var textSize = g.MeasureString(letter, font);
        g.DrawString(letter, font, Brushes.White, (16 - textSize.Width) / 2, (16 - textSize.Height) / 2 - 1);
        g.FillEllipse(brush, 9, 9, 7, 7);
    }

    private static void DrawBatteryBarStyle(Graphics g, int percent, Color color)
    {
        using var brush = new SolidBrush(color);
        g.DrawRectangle(Pens.Gray, 1, 4, 12, 8);
        g.FillRectangle(Brushes.Gray, 13, 6, 2, 4);
        int fillWidth = (int)Math.Round(10 * (percent / 100.0));
        g.FillRectangle(brush, 2, 5, Math.Max(0, fillWidth), 6);
    }
}

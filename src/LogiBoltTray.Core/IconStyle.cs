namespace LogiBoltTray.Core;

// D_Hybrid (small device-type glyph + percent number) was removed after real-hardware testing
// showed GDI+ (System.Drawing) cannot render color-emoji glyphs at all at 16x16 — the mouse glyph
// didn't render, the keyboard glyph rendered as unreadable tofu. B_GlyphBadge kept the same
// constraint in mind: it now draws a plain letter instead of an emoji (see TrayBitmapRenderer).
public enum IconStyle
{
    A_Number,
    B_GlyphBadge,
    C_BatteryBar,
}

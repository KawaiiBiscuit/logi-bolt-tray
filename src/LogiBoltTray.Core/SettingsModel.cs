namespace LogiBoltTray.Core;

public sealed class SettingsModel
{
    public int PollIntervalSeconds { get; set; } = 60;
    public IconStyle IconStyle { get; set; } = IconStyle.A_Number;
    public int LowBatteryThresholdPercent { get; set; } = 20;
    public bool LowBatteryNotificationsEnabled { get; set; } = true;
    public bool AutostartEnabled { get; set; } = false;
    public Dictionary<string, bool> DeviceVisibility { get; set; } = new();

    public static SettingsModel Default() => new();
}

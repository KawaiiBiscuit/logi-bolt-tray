namespace LogiBoltTray.Protocol;

public readonly record struct BatteryStatus(int Percent, bool IsCharging, bool IsUnknown)
{
    public static BatteryStatus Unknown() => new(Percent: 0, IsCharging: false, IsUnknown: true);
}

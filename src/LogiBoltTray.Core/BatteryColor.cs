namespace LogiBoltTray.Core;

public enum BatteryColor
{
    Red,
    Orange,
    Green,
    Purple,
}

public static class BatteryColorPolicy
{
    public static BatteryColor GetColor(int percent)
    {
        if (percent == 100) return BatteryColor.Purple;
        if (percent > 60) return BatteryColor.Green;
        if (percent >= 20) return BatteryColor.Orange;
        return BatteryColor.Red;
    }
}

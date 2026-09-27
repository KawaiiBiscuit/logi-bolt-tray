namespace LogiBoltTray.Protocol;

/// <summary>
/// Approximate Li-Po discharge curve used by Logitech HID++ devices that only expose raw
/// voltage (feature 0x1001) instead of a computed percentage. Anchor points are widely
/// published approximations for a single-cell 3.7V Li-Po; exact shape may need tuning per
/// real device once tested on hardware (see spec's "outside MVP" iteration note).
/// </summary>
public static class BatteryVoltageTable
{
    private static readonly (int Millivolts, int Percent)[] Anchors =
    {
        (4200, 100),
        (4186, 100),
        (4067, 90),
        (3989, 80),
        (3922, 70),
        (3859, 60),
        (3811, 50),
        (3778, 40),
        (3751, 30),
        (3717, 20),
        (3671, 10),
        (3646, 5),
        (3579, 2),
        (3500, 0),
    };

    public static int ToPercent(int millivolts)
    {
        if (millivolts >= Anchors[0].Millivolts)
        {
            return 100;
        }

        if (millivolts <= Anchors[^1].Millivolts)
        {
            return 0;
        }

        for (int i = 0; i < Anchors.Length - 1; i++)
        {
            (int hiMv, int hiPct) = Anchors[i];
            (int loMv, int loPct) = Anchors[i + 1];

            if (millivolts <= hiMv && millivolts >= loMv)
            {
                double fraction = (double)(millivolts - loMv) / (hiMv - loMv);
                return loPct + (int)Math.Round(fraction * (hiPct - loPct));
            }
        }

        return 0;
    }
}

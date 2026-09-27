using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class BatteryColorTests
{
    [Theory]
    [InlineData(0, BatteryColor.Red)]
    [InlineData(19, BatteryColor.Red)]
    [InlineData(20, BatteryColor.Orange)]
    [InlineData(45, BatteryColor.Orange)]
    [InlineData(60, BatteryColor.Orange)]
    [InlineData(61, BatteryColor.Green)]
    [InlineData(99, BatteryColor.Green)]
    [InlineData(100, BatteryColor.Purple)]
    public void GetColor_MatchesSpecThresholds(int percent, BatteryColor expected)
    {
        Assert.Equal(expected, BatteryColorPolicy.GetColor(percent));
    }
}

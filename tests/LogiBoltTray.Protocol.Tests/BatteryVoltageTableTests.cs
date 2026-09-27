using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class BatteryVoltageTableTests
{
    [Theory]
    [InlineData(4200, 100)]
    [InlineData(3500, 0)]
    [InlineData(4500, 100)] // above table max clamps to 100
    [InlineData(3000, 0)]   // below table min clamps to 0
    public void ToPercent_KnownAnchorPoints_ReturnsExactValue(int millivolts, int expectedPercent)
    {
        Assert.Equal(expectedPercent, BatteryVoltageTable.ToPercent(millivolts));
    }

    [Fact]
    public void ToPercent_BetweenAnchorPoints_InterpolatesLinearly()
    {
        // Table has anchors at 4186mV->100% and 4067mV->90%; midpoint should be ~95%.
        int midpoint = (4186 + 4067) / 2;

        int percent = BatteryVoltageTable.ToPercent(midpoint);

        Assert.InRange(percent, 93, 97);
    }
}

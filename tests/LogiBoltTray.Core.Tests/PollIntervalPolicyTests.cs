using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class PollIntervalPolicyTests
{
    [Theory]
    [InlineData(60, 60)]
    [InlineData(1, 1)]
    [InlineData(1800, 1800)]
    [InlineData(0, 1)]      // below min clamps up
    [InlineData(-5, 1)]     // negative also clamps up
    [InlineData(3600, 1800)] // above max clamps down
    public void Clamp_ReturnsValueWithinAllowedRange(int requested, int expected)
    {
        Assert.Equal(expected, PollIntervalPolicy.Clamp(requested));
    }
}

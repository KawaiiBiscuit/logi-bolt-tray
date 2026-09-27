using LogiBoltTray.Core;
using Xunit;

namespace LogiBoltTray.Core.Tests;

public class LowBatteryNotifierTests
{
    [Fact]
    public void ShouldNotify_FirstReadingBelowThreshold_ReturnsTrueOnce()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.True(notifier.ShouldNotify("mouse-1", 15));
        Assert.False(notifier.ShouldNotify("mouse-1", 14)); // still below threshold, already notified
    }

    [Fact]
    public void ShouldNotify_StaysAboveThreshold_NeverNotifies()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.False(notifier.ShouldNotify("mouse-1", 80));
        Assert.False(notifier.ShouldNotify("mouse-1", 70));
    }

    [Fact]
    public void ShouldNotify_RisesAboveThresholdThenDropsAgain_NotifiesAgain()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.True(notifier.ShouldNotify("mouse-1", 10));
        Assert.False(notifier.ShouldNotify("mouse-1", 5));
        // charged back up above threshold
        Assert.False(notifier.ShouldNotify("mouse-1", 90));
        // dropped below threshold again — should notify again
        Assert.True(notifier.ShouldNotify("mouse-1", 12));
    }

    [Fact]
    public void ShouldNotify_TracksEachDeviceIndependently()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);

        Assert.True(notifier.ShouldNotify("mouse-1", 10));
        Assert.True(notifier.ShouldNotify("keyboard-1", 5)); // different device, notifies independently
    }

    [Fact]
    public void UpdateThreshold_ChangesThresholdWithoutResettingNotifiedState()
    {
        var notifier = new LowBatteryNotifier(thresholdPercent: 20);
        Assert.True(notifier.ShouldNotify("mouse-1", 15)); // notifies once at threshold 20

        notifier.UpdateThreshold(10);

        Assert.False(notifier.ShouldNotify("mouse-1", 12)); // above new threshold 10, no notification
        Assert.True(notifier.ShouldNotify("mouse-1", 8));   // below new threshold, notifies again
    }
}

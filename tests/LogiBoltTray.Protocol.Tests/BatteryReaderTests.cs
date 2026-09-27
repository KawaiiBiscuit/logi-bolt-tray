using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class BatteryReaderTests
{
    private const byte DeviceIndex = 0x01;

    [Fact]
    public void Read_Feature0x1000Supported_ReturnsPercentAndChargingFromBatteryStatus()
    {
        var transport = new FakeHidppTransport();
        // RootFeatureClient.FindFeatureIndex(0x1000) -> featureIndex 0x05
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x05, 0x00, 0x00));
        // BatteryReader calls function 0x00 on featureIndex 0x05 -> [percent, nextLevel, chargingStatus]
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x05, 0x00, 0x1, 87, 80, 0x01)); // chargingStatus 1 = recharging

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.False(status.IsUnknown);
        Assert.Equal(87, status.Percent);
        Assert.True(status.IsCharging);
    }

    [Fact]
    public void Read_Only0x1004Supported_FallsBackToUnifiedBattery()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x1000 not found
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x07, 0x00, 0x00)); // 0x1004 -> index 0x07
        // GetStatus (function 0x01) -> [stateOfCharge, batteryLevel, chargingStatus]
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x07, 0x01, 0x1, 42, 0x02, 0x00));

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.False(status.IsUnknown);
        Assert.Equal(42, status.Percent);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void Read_Only0x1001Supported_FallsBackToVoltageTable()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x1000 not found
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x1004 not found
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x09, 0x00, 0x00)); // 0x1001 -> index 0x09
        // GetBatteryVoltage -> [mV high, mV low, chargingFlag]
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x09, 0x00, 0x1, 0x10, 0x5E, 0x01)); // 0x105E = 4190mV, charging

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.False(status.IsUnknown);
        Assert.Equal(100, status.Percent);
        Assert.True(status.IsCharging);
    }

    [Fact]
    public void Read_NoBatteryFeatureSupported_ReturnsUnknown()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00));
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00));
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00));

        var status = new BatteryReader(transport, DeviceIndex).Read();

        Assert.True(status.IsUnknown);
    }
}

using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class DeviceDiscoveryServiceTests
{
    private static HidppFrame RootResponse(byte deviceIndex, byte featureIndex)
        => HidppFrame.Short(deviceIndex, 0x00, 0x00, 0x1, featureIndex, 0x00, 0x00);

    [Fact]
    public void DiscoverDevices_OneDeviceRespondingAtIndexOne_ReturnsSingleDevice()
    {
        var transport = new FakeHidppTransport();

        // Device index 0x01: BatteryReader tries 0x1000 first (found at 0x05)
        transport.EnqueueResponse(RootResponse(0x01, 0x05));
        transport.EnqueueResponse(HidppFrame.Short(0x01, 0x05, 0x00, 0x1, 76, 70, 0x00));
        // DeviceNameClient: 0x0005 found at 0x03, length 2, name "M5"
        transport.EnqueueResponse(RootResponse(0x01, 0x03));
        transport.EnqueueResponse(HidppFrame.Short(0x01, 0x03, 0x00, 0x1, 2, 0, 0));
        byte[] nameBytes = new byte[16];
        nameBytes[0] = (byte)'M';
        nameBytes[1] = (byte)'5';
        transport.EnqueueResponse(HidppFrame.Long(0x01, 0x03, 0x01, 0x1, nameBytes));
        // No second root lookup here: DeviceNameClient caches the 0x0005 feature index, so
        // GetDeviceType() reuses the one resolved for GetName() on the same instance.
        transport.EnqueueResponse(HidppFrame.Short(0x01, 0x03, 0x02, 0x1, 0x03, 0, 0)); // kind = mouse (0x03, confirmed against real hardware)

        // Device indices 0x02..0x06: no response at all (not paired)
        // FakeHidppTransport returns null automatically once its queue is empty.

        var devices = new DeviceDiscoveryService(transport).DiscoverDevices();

        var device = Assert.Single(devices);
        Assert.Equal((byte)0x01, device.DeviceIndex);
        Assert.Equal("M5", device.Name);
        Assert.Equal(DeviceType.Mouse, device.Type);
        Assert.Equal(76, device.Battery.Percent);
    }

    [Fact]
    public void DiscoverDevices_NoDeviceResponds_ReturnsEmptyList()
    {
        var transport = new FakeHidppTransport();

        var devices = new DeviceDiscoveryService(transport).DiscoverDevices();

        Assert.Empty(devices);
    }
}

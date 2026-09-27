using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class DeviceNameClientTests
{
    private const byte DeviceIndex = 0x02;

    [Fact]
    public void GetName_ReadsLengthThenReadsThatManyCharacters()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x03, 0x00, 0x00)); // 0x0005 -> index 0x03
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x03, 0x00, 0x1, 6, 0, 0));           // GetLength -> 6 chars
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes("MX Master".PadRight(16, '\0'))[..16];
        transport.EnqueueResponse(HidppFrame.Long(DeviceIndex, 0x03, 0x01, 0x1, nameBytes));           // GetName(0)

        string? name = new DeviceNameClient(transport, DeviceIndex).GetName();

        Assert.Equal("MX Mas", name); // first 6 chars requested + null terminator boundary honored: "MX Mas" + 't' truncated at length 6 -> see note below
    }

    [Fact]
    public void GetDeviceType_MouseKindByte_MapsToMouse()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x03, 0x00, 0x00)); // 0x0005 -> index 0x03
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x03, 0x02, 0x1, 0x03, 0, 0));        // GetKind -> 0x03 = mouse (confirmed against real MX Master 3S hardware)

        var type = new DeviceNameClient(transport, DeviceIndex).GetDeviceType();

        Assert.Equal(DeviceType.Mouse, type);
    }

    [Fact]
    public void GetNameThenGetDeviceType_ResolvesTheFeatureIndexOnlyOnce()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x03, 0x00, 0x00)); // root: 0x0005 -> index 0x03
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x03, 0x00, 0x1, 6, 0, 0));           // GetLength -> 6 chars
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes("MX Master".PadRight(16, '\0'))[..16];
        transport.EnqueueResponse(HidppFrame.Long(DeviceIndex, 0x03, 0x01, 0x1, nameBytes));          // GetName
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x03, 0x02, 0x1, 0x03, 0, 0));        // GetKind -> mouse

        var client = new DeviceNameClient(transport, DeviceIndex);
        string? name = client.GetName();
        DeviceType type = client.GetDeviceType();

        Assert.Equal("MX Mas", name);
        Assert.Equal(DeviceType.Mouse, type);

        // The root-feature lookup is addressed to feature index 0x00 with function 0x00 — it must
        // be sent exactly once for the instance, not once per public method.
        int rootLookups = transport.SentRequests.Count(r => r.FeatureIndex == 0x00 && r.FunctionId == 0x00);
        Assert.Equal(1, rootLookups);
        Assert.Equal(4, transport.SentRequests.Count); // root lookup + GetLength + GetName + GetKind
    }

    [Fact]
    public void GetDeviceType_FeatureNotSupported_ReturnsUnknown()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(DeviceIndex, 0x00, 0x00, 0x1, 0x00, 0x00, 0x00)); // 0x0005 not found

        var type = new DeviceNameClient(transport, DeviceIndex).GetDeviceType();

        Assert.Equal(DeviceType.Unknown, type);
    }
}

using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class RootFeatureClientTests
{
    [Fact]
    public void FindFeatureIndex_SendsGetFeatureRequestToRootFeatureIndexZero()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x10, p1: 0x00, p2: 0x03));
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        byte? index = client.FindFeatureIndex(featureId: 0x1000);

        Assert.Equal((byte)0x10, index);
        var sent = Assert.Single(transport.SentRequests);
        Assert.Equal(0x00, sent.FeatureIndex); // root feature is always index 0
        Assert.Equal(0x00, sent.FunctionId);   // GetFeature is function 0 on IRoot
        Assert.Equal(0x10, sent.Params[0]);    // featureId high byte
        Assert.Equal(0x00, sent.Params[1]);    // featureId low byte
    }

    [Fact]
    public void FindFeatureIndex_UnsupportedFeature_ReturnsNull()
    {
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x00, p1: 0x00, p2: 0x00));
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        byte? index = client.FindFeatureIndex(featureId: 0x1000);

        Assert.Null(index); // featureIndex 0x00 in the response means "not found"
    }

    [Fact]
    public void FindFeatureIndex_NoResponse_ReturnsNull()
    {
        var transport = new FakeHidppTransport(); // no response enqueued
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        byte? index = client.FindFeatureIndex(featureId: 0x1000);

        Assert.Null(index);
    }

    [Fact]
    public void FindFeatureIndex_ConsecutiveCalls_UseDifferentSoftwareIds()
    {
        // Every call used to hardcode the same SoftwareId, which is exactly what let a stale
        // response to one lookup get accepted as the answer to a different one (see
        // HidppFrameMatchesTests and SoftwareIdSequence's doc comment for the real-hardware bug
        // this caused). Two consecutive lookups must use two different SoftwareIds so
        // HidppFrame.Matches can actually tell them apart.
        var transport = new FakeHidppTransport();
        transport.EnqueueResponse(HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x10, p1: 0x00, p2: 0x03));
        transport.EnqueueResponse(HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x08, p1: 0x00, p2: 0x03));
        var client = new RootFeatureClient(transport, deviceIndex: 0x01);

        client.FindFeatureIndex(featureId: 0x1000);
        client.FindFeatureIndex(featureId: 0x1004);

        Assert.Equal(2, transport.SentRequests.Count);
        Assert.NotEqual(transport.SentRequests[0].SoftwareId, transport.SentRequests[1].SoftwareId);
    }
}

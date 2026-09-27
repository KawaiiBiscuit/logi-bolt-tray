using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class HidppFrameMatchesTests
{
    private const byte DeviceIndex = 0x02;

    private static HidppFrame ErrorResponse(byte deviceIndex, byte addressedFeatureIndex, byte functionId, byte softwareId, byte errorCode)
        => HidppFrame.Parse(new byte[]
        {
            HidppFrame.ShortReportId,
            deviceIndex,
            0xFF, // error marker
            addressedFeatureIndex,
            (byte)((functionId << 4) | (softwareId & 0x0F)),
            errorCode,
            0x00,
        });

    [Fact]
    public void Matches_NormalResponseWithSameAddressing_IsTrue()
    {
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var response = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1, p0: 76);

        Assert.True(HidppFrame.Matches(request, response));
    }

    [Fact]
    public void Matches_ErrorResponseAddressedToTheRequestedFeature_IsTrue()
    {
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var response = ErrorResponse(DeviceIndex, addressedFeatureIndex: 0x05, functionId: 0x00, softwareId: 0x1, errorCode: 0x05);

        Assert.True(response.IsError);
        Assert.Equal((byte)0x05, response.AddressedFeatureIndex);
        Assert.True(HidppFrame.Matches(request, response));
    }

    [Fact]
    public void Matches_ErrorResponseAddressedToADifferentFeature_IsFalse()
    {
        // The bug this guards: a previous request to feature 0x0A timed out, its late error reply
        // is still queued, and the next request (feature 0x05) shares the identical
        // device/function/software-id header — RootFeatureClient issues exactly that header for
        // every feature lookup on a device. The stale error must not be accepted as this reply.
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var staleResponse = ErrorResponse(DeviceIndex, addressedFeatureIndex: 0x0A, functionId: 0x00, softwareId: 0x1, errorCode: 0x05);

        Assert.False(HidppFrame.Matches(request, staleResponse));
    }

    [Fact]
    public void Matches_ResponseForADifferentDeviceIndex_IsFalse()
    {
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var response = HidppFrame.Short(deviceIndex: 0x03, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);

        Assert.False(HidppFrame.Matches(request, response));
    }

    [Fact]
    public void Matches_ResponseForADifferentFunctionId_IsFalse()
    {
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var response = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x01, softwareId: 0x1);

        Assert.False(HidppFrame.Matches(request, response));
    }

    [Fact]
    public void Matches_UnsolicitedNotificationWithADifferentSoftwareId_IsFalse()
    {
        // Device-initiated notifications carry softwareId 0 and must never be mistaken for a reply.
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var notification = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x0);

        Assert.False(HidppFrame.Matches(request, notification));
    }

    [Fact]
    public void Matches_NormalResponseForADifferentFeatureIndex_IsFalse()
    {
        var request = HidppFrame.Short(DeviceIndex, featureIndex: 0x05, functionId: 0x00, softwareId: 0x1);
        var response = HidppFrame.Short(DeviceIndex, featureIndex: 0x06, functionId: 0x00, softwareId: 0x1);

        Assert.False(HidppFrame.Matches(request, response));
    }

    [Fact]
    public void Matches_TwoDifferentRootFeatureLookups_RejectCrossMatchOnlyWhenSoftwareIdsDiffer()
    {
        // Root feature (GetFeature) lookups for two DIFFERENT feature IDs — e.g. "does this device
        // support 0x1000?" then "does it support 0x1004?" — are indistinguishable at the header
        // level: both address FeatureIndex 0x00 (root), FunctionId 0x00 (GetFeature). Only a
        // distinct SoftwareId per lookup lets Matches tell a late reply to the FIRST question apart
        // from the answer to the SECOND. This is exactly the real-hardware bug SoftwareIdSequence
        // fixes: a keyboard's genuine "0x1004 supported, 100%, charging" answer was discarded
        // almost every poll in favour of a stale "0x1000 not found" reply from moments earlier,
        // because every caller used to hardcode the same SoftwareId for every request.
        var requestForSecondLookup = HidppFrame.Short(DeviceIndex, featureIndex: 0x00, functionId: 0x00, softwareId: 0x2, p0: 0x10, p1: 0x04); // "is 0x1004 supported?"
        var staleResponseToFirstLookup = HidppFrame.Short(DeviceIndex, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x00); // late "0x1000 not found" reply

        Assert.False(HidppFrame.Matches(requestForSecondLookup, staleResponseToFirstLookup));

        // Demonstrates why the bug existed: with the OLD hardcoded-constant SoftwareId, the exact
        // same stale response WOULD have been accepted as the second lookup's answer.
        var requestWithTheOldSharedSoftwareId = HidppFrame.Short(DeviceIndex, featureIndex: 0x00, functionId: 0x00, softwareId: 0x1, p0: 0x10, p1: 0x04);
        Assert.True(HidppFrame.Matches(requestWithTheOldSharedSoftwareId, staleResponseToFirstLookup));
    }

    [Fact]
    public void Parse_ErrorResponse_ExposesAddressedFeatureIndex()
    {
        var frame = ErrorResponse(DeviceIndex, addressedFeatureIndex: 0x0A, functionId: 0x00, softwareId: 0x1, errorCode: 0x05);

        Assert.True(frame.IsError);
        Assert.Equal((byte)0x0A, frame.AddressedFeatureIndex);
        Assert.Equal((byte)0x05, frame.ErrorCode);
    }

    [Fact]
    public void Parse_NormalResponse_HasNoAddressedFeatureIndex()
    {
        var frame = HidppFrame.Parse(new byte[] { 0x10, 0x01, 0x02, 0xA3, 0x11, 0x22, 0x33 });

        Assert.False(frame.IsError);
        Assert.Null(frame.AddressedFeatureIndex);
    }
}

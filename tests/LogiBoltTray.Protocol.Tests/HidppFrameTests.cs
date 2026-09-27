using LogiBoltTray.Protocol;
using Xunit;

namespace LogiBoltTray.Protocol.Tests;

public class HidppFrameTests
{
    [Fact]
    public void Short_BuildsSevenByteReportWithHeaderAndParams()
    {
        var frame = HidppFrame.Short(deviceIndex: 0x01, featureIndex: 0x02, functionId: 0x0A, softwareId: 0x3, p0: 0x11, p1: 0x22, p2: 0x33);

        byte[] bytes = frame.ToBytes();

        Assert.Equal(new byte[] { 0x10, 0x01, 0x02, 0xA3, 0x11, 0x22, 0x33 }, bytes);
    }

    [Fact]
    public void Long_BuildsTwentyByteReportWithSixteenByteParams()
    {
        var p = new byte[16];
        p[0] = 0xAA;
        p[15] = 0xBB;
        var frame = HidppFrame.Long(deviceIndex: 0x02, featureIndex: 0x05, functionId: 0x1, softwareId: 0x2, p);

        byte[] bytes = frame.ToBytes();

        Assert.Equal(20, bytes.Length);
        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0x02, bytes[1]);
        Assert.Equal(0x05, bytes[2]);
        Assert.Equal(0x12, bytes[3]);
        Assert.Equal(0xAA, bytes[4]);
        Assert.Equal(0xBB, bytes[19]);
    }

    [Fact]
    public void Parse_NormalShortResponse_IsNotError()
    {
        byte[] raw = { 0x10, 0x01, 0x02, 0xA3, 0x11, 0x22, 0x33 };

        var frame = HidppFrame.Parse(raw);

        Assert.False(frame.IsError);
        Assert.Equal(0x01, frame.DeviceIndex);
        Assert.Equal(0x02, frame.FeatureIndex);
        Assert.Equal(0x0A, frame.FunctionId);
        Assert.Equal(0x3, frame.SoftwareId);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, frame.Params);
    }

    [Fact]
    public void Parse_NormalLongResponse_RoundTripsAllSixteenParamBytes()
    {
        // A 20-byte long report: [0x11, deviceIndex, featureIndex, functionId_softwareId, 16 param bytes].
        // Device-name responses come back in this shape even though the request is a short report —
        // the transport must therefore always read into a 20-byte buffer or the name gets truncated.
        var payload = new byte[16];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(0xE0 + i);
        }

        byte[] raw = new byte[20];
        raw[0] = HidppFrame.LongReportId;
        raw[1] = 0x02;
        raw[2] = 0x05;
        raw[3] = 0x71; // functionId 0x7, softwareId 0x1
        payload.CopyTo(raw, 4);

        var frame = HidppFrame.Parse(raw);

        Assert.False(frame.IsError);
        Assert.Equal(HidppFrame.LongReportId, frame.ReportId);
        Assert.Equal(0x02, frame.DeviceIndex);
        Assert.Equal(0x05, frame.FeatureIndex);
        Assert.Equal(0x7, frame.FunctionId);
        Assert.Equal(0x1, frame.SoftwareId);
        Assert.Equal(16, frame.Params.Length);
        Assert.Equal(payload, frame.Params);
    }

    [Fact]
    public void Parse_LongResponseCarryingDeviceName_KeepsFullName()
    {
        // Regression guard for the truncating read buffer: a 16-byte name payload must survive
        // parsing intact rather than collapsing to the 3 params a short report would carry.
        byte[] raw = new byte[20];
        raw[0] = HidppFrame.LongReportId;
        raw[1] = 0x01;
        raw[2] = 0x03;
        raw[3] = 0x11;
        System.Text.Encoding.ASCII.GetBytes("MX Master 3S Mous").AsSpan(0, 16).CopyTo(raw.AsSpan(4));

        var frame = HidppFrame.Parse(raw);

        Assert.Equal("MX Master 3S Mou", System.Text.Encoding.ASCII.GetString(frame.Params));
    }

    [Fact]
    public void Parse_ErrorResponse_SetsIsErrorAndErrorCode()
    {
        // [reportId, deviceIndex, 0xFF (error marker), addressedFeatureIndex, functionId_softwareId, errorCode, pad]
        byte[] raw = { 0x10, 0x01, 0xFF, 0x02, 0xA3, 0x05, 0x00 };

        var frame = HidppFrame.Parse(raw);

        Assert.True(frame.IsError);
        Assert.Equal((byte)0x05, frame.ErrorCode);
    }
}

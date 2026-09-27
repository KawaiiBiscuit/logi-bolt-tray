namespace LogiBoltTray.Protocol;

public readonly struct HidppFrame
{
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    private const byte ErrorFeatureMarker = 0xFF;

    public byte ReportId { get; }
    public byte DeviceIndex { get; }
    public byte FeatureIndex { get; }
    public byte FunctionId { get; }
    public byte SoftwareId { get; }
    public byte[] Params { get; }

    /// <summary>
    /// The feature index an error response was addressed to (byte 3 of an error frame). Only
    /// meaningful when <see cref="IsError"/>; <c>null</c> on normal responses and outgoing frames,
    /// whose own feature index lives in <see cref="FeatureIndex"/>.
    /// </summary>
    public byte? AddressedFeatureIndex { get; }

    public bool IsError => FeatureIndex == ErrorFeatureMarker;
    public byte? ErrorCode => IsError ? Params[0] : null;

    private HidppFrame(byte reportId, byte deviceIndex, byte featureIndex, byte functionId, byte softwareId, byte[] paramBytes, byte? addressedFeatureIndex = null)
    {
        ReportId = reportId;
        DeviceIndex = deviceIndex;
        FeatureIndex = featureIndex;
        FunctionId = functionId;
        SoftwareId = softwareId;
        Params = paramBytes;
        AddressedFeatureIndex = addressedFeatureIndex;
    }

    /// <summary>
    /// True when <paramref name="response"/> is a reply to <paramref name="request"/>.
    /// </summary>
    /// <remarks>
    /// Error frames carry 0xFF in the feature-index slot and name the feature they were addressed
    /// to in <see cref="AddressedFeatureIndex"/>, so they must be matched on that instead. Accepting
    /// any error frame with the right device/function/software id (as this predicate used to) is a
    /// real misattribution risk: a request that times out leaves its late response in the HID read
    /// queue, and consecutive lookups are near-identical on the wire — RootFeatureClient sends the
    /// same DeviceIndex + FeatureIndex 0x00 + FunctionId 0x00 + SoftwareId 0x1 header for every
    /// feature lookup on a device, differing only in the params.
    /// </remarks>
    public static bool Matches(HidppFrame request, HidppFrame response)
    {
        if (response.DeviceIndex != request.DeviceIndex
            || response.FunctionId != request.FunctionId
            || response.SoftwareId != request.SoftwareId)
        {
            return false;
        }

        if (response.IsError)
        {
            return response.AddressedFeatureIndex == request.FeatureIndex;
        }

        return response.FeatureIndex == request.FeatureIndex;
    }

    public static HidppFrame Short(byte deviceIndex, byte featureIndex, byte functionId, byte softwareId, byte p0 = 0, byte p1 = 0, byte p2 = 0)
        => new(ShortReportId, deviceIndex, featureIndex, functionId, softwareId, new[] { p0, p1, p2 });

    public static HidppFrame Long(byte deviceIndex, byte featureIndex, byte functionId, byte softwareId, byte[] params16)
    {
        if (params16.Length != 16)
        {
            throw new ArgumentException("Long HID++ reports carry exactly 16 parameter bytes.", nameof(params16));
        }

        return new HidppFrame(LongReportId, deviceIndex, featureIndex, functionId, softwareId, params16);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[4 + Params.Length];
        bytes[0] = ReportId;
        bytes[1] = DeviceIndex;
        bytes[2] = FeatureIndex;
        bytes[3] = (byte)((FunctionId << 4) | (SoftwareId & 0x0F));
        Array.Copy(Params, 0, bytes, 4, Params.Length);
        return bytes;
    }

    public static HidppFrame Parse(ReadOnlySpan<byte> raw)
    {
        byte reportId = raw[0];
        byte deviceIndex = raw[1];
        byte featureIndex = raw[2];

        byte functionId;
        byte softwareId;
        byte[] paramBytes;
        byte? addressedFeatureIndex = null;

        if (featureIndex == ErrorFeatureMarker)
        {
            // Error response: [reportId, deviceIndex, 0xFF, addressedFeatureIndex, functionId_softwareId, errorCode, ...]
            addressedFeatureIndex = raw[3];
            byte functionAndSoftwareId = raw[4];
            functionId = (byte)(functionAndSoftwareId >> 4);
            softwareId = (byte)(functionAndSoftwareId & 0x0F);
            paramBytes = raw[5..].ToArray();
        }
        else
        {
            // Normal response: [reportId, deviceIndex, featureIndex, functionId_softwareId, param0, param1, ...]
            byte functionAndSoftwareId = raw[3];
            functionId = (byte)(functionAndSoftwareId >> 4);
            softwareId = (byte)(functionAndSoftwareId & 0x0F);
            paramBytes = raw[4..].ToArray();
        }

        return new HidppFrame(reportId, deviceIndex, featureIndex, functionId, softwareId, paramBytes, addressedFeatureIndex);
    }
}

namespace LogiBoltTray.Protocol;

public sealed class RootFeatureClient
{
    private const byte RootFeatureIndex = 0x00;
    private const byte GetFeatureFunctionId = 0x00;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidppTransport _transport;
    private readonly byte _deviceIndex;

    public RootFeatureClient(IHidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        _deviceIndex = deviceIndex;
    }

    public byte? FindFeatureIndex(ushort featureId)
    {
        byte high = (byte)(featureId >> 8);
        byte low = (byte)(featureId & 0xFF);
        // A fresh SoftwareId per call, not a shared constant — see SoftwareIdSequence's doc comment
        // for why reusing one lets a stale response to a DIFFERENT feature lookup get accepted here.
        var request = HidppFrame.Short(_deviceIndex, RootFeatureIndex, GetFeatureFunctionId, SoftwareIdSequence.Next(), high, low, 0);

        HidppFrame? response = _transport.SendAndReceive(request, DefaultTimeout);
        if (response is null || response.Value.IsError)
        {
            return null;
        }

        byte featureIndex = response.Value.Params[0];
        return featureIndex == 0x00 ? null : featureIndex;
    }
}

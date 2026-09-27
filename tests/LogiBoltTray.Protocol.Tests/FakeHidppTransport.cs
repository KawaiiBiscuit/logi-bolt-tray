namespace LogiBoltTray.Protocol.Tests;

using LogiBoltTray.Protocol;

public sealed class FakeHidppTransport : IHidppTransport
{
    private readonly Queue<HidppFrame> _responses = new();

    public List<HidppFrame> SentRequests { get; } = new();

    public void EnqueueResponse(HidppFrame response) => _responses.Enqueue(response);

    public HidppFrame? SendAndReceive(HidppFrame request, TimeSpan timeout)
    {
        SentRequests.Add(request);
        return _responses.Count > 0 ? _responses.Dequeue() : null;
    }
}

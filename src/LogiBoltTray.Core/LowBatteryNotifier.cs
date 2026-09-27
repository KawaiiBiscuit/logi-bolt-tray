namespace LogiBoltTray.Core;

public sealed class LowBatteryNotifier
{
    private int _thresholdPercent;
    private readonly Dictionary<string, bool> _alreadyNotifiedBelowThreshold = new();

    public LowBatteryNotifier(int thresholdPercent)
    {
        _thresholdPercent = thresholdPercent;
    }

    public void UpdateThreshold(int thresholdPercent) => _thresholdPercent = thresholdPercent;

    public bool ShouldNotify(string deviceKey, int currentPercent)
    {
        bool isBelow = currentPercent < _thresholdPercent;
        bool alreadyNotified = _alreadyNotifiedBelowThreshold.GetValueOrDefault(deviceKey);

        if (!isBelow)
        {
            _alreadyNotifiedBelowThreshold[deviceKey] = false;
            return false;
        }

        if (alreadyNotified)
        {
            return false;
        }

        _alreadyNotifiedBelowThreshold[deviceKey] = true;
        return true;
    }
}

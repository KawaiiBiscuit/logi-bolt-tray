namespace LogiBoltTray.Core;

public static class PollIntervalPolicy
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 1800; // 30 minutes

    public static int Clamp(int requestedSeconds) => Math.Clamp(requestedSeconds, MinSeconds, MaxSeconds);
}

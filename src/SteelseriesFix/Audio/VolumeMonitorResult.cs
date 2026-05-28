namespace SteelseriesFix.Audio;

public sealed record VolumeMonitorResult(
    bool IsConfigured,
    int HighSessions,
    int UpdatedSessions,
    string Message)
{
    public bool ChangedVolume => UpdatedSessions > 0;
}

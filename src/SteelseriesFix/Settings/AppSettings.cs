using SteelseriesFix.Audio;

namespace SteelseriesFix.Settings;

public sealed class AppSettings
{
    public string? PlaybackEndpointId { get; set; }

    public string? SonarMicrophonePlaybackEndpointId { get; set; }

    public string? CaptureEndpointId { get; set; }

    public List<string> TargetProcessNames { get; set; } = DiscordProcessMatcher.DefaultProcessNames.ToList();

    public bool AutoMonitorEnabled { get; set; } = true;

    public bool RunAtStartup { get; set; } = true;

    public int MonitorIntervalSeconds { get; set; } = 3;

    public float MonitorVolumeThreshold { get; set; } = 0.001f;

    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public static AppSettings CreateDefault() => new()
    {
        TargetProcessNames = DiscordProcessMatcher.DefaultProcessNames.ToList()
    };

    public AppSettings Normalize()
    {
        TargetProcessNames = DiscordProcessMatcher.NormalizeTargets(TargetProcessNames).ToList();
        MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 1, 60);
        MonitorVolumeThreshold = Math.Clamp(MonitorVolumeThreshold, 0.0f, 1.0f);
        return this;
    }
}

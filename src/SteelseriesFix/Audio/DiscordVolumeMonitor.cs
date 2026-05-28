using SteelseriesFix.Settings;

namespace SteelseriesFix.Audio;

public sealed class DiscordVolumeMonitor(IAudioService audioService)
{
    public VolumeMonitorResult CheckAndFix(AppSettings settings)
    {
        settings.Normalize();

        if (string.IsNullOrWhiteSpace(settings.PlaybackEndpointId) ||
            string.IsNullOrWhiteSpace(settings.SonarMicrophonePlaybackEndpointId))
        {
            return new VolumeMonitorResult(false, 0, 0, "Monitoring is waiting for saved devices.");
        }

        var playbackEndpoint = new AudioEndpoint(settings.PlaybackEndpointId, "Saved playback mixer", AudioEndpointKind.Playback);
        var sonarMicrophoneEndpoint = new AudioEndpoint(settings.SonarMicrophonePlaybackEndpointId, "Saved Sonar microphone mixer", AudioEndpointKind.Playback);

        var targets = DiscordProcessMatcher.NormalizeTargets(settings.TargetProcessNames);
        int playbackHighSessions;
        int sonarHighSessions;

        try
        {
            playbackHighSessions = CountHighDiscordSessions(playbackEndpoint, targets, settings.MonitorVolumeThreshold);
            sonarHighSessions = CountHighDiscordSessions(sonarMicrophoneEndpoint, targets, settings.MonitorVolumeThreshold);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return new VolumeMonitorResult(false, 0, 0, "Monitoring is waiting for the saved devices to be available.");
        }

        var highSessions = playbackHighSessions + sonarHighSessions;

        if (highSessions == 0)
        {
            return new VolumeMonitorResult(true, 0, 0, "Discord volume is already muted on the watched devices.");
        }

        var updatedSessions = 0;
        if (playbackHighSessions > 0)
        {
            updatedSessions += audioService.SetDiscordVolumeToZero(playbackEndpoint, targets).UpdatedSessions;
        }

        if (sonarHighSessions > 0)
        {
            updatedSessions += audioService.SetDiscordVolumeToZero(sonarMicrophoneEndpoint, targets).UpdatedSessions;
        }

        return new VolumeMonitorResult(
            true,
            highSessions,
            updatedSessions,
            $"Auto-muted Discord on {updatedSessions} session(s).");
    }

    private int CountHighDiscordSessions(
        AudioEndpoint endpoint,
        IReadOnlyCollection<string> targetProcessNames,
        float threshold)
    {
        return audioService.GetSessions(endpoint, targetProcessNames)
            .Count(session => session.IsTargetDiscord &&
                              session.MasterVolume is float volume &&
                              volume > threshold);
    }
}

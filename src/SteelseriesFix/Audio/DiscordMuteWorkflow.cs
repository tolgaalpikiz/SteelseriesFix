namespace SteelseriesFix.Audio;

public sealed class DiscordMuteWorkflow(IAudioService audioService)
{
    public MuteDiscordResult Apply(AudioEndpoint playbackEndpoint, AudioEndpoint sonarMicrophoneEndpoint, IEnumerable<string>? targetProcessNames)
    {
        if (playbackEndpoint.Kind != AudioEndpointKind.Playback)
        {
            throw new ArgumentException("The playback endpoint must be a playback device.", nameof(playbackEndpoint));
        }

        if (sonarMicrophoneEndpoint.Kind != AudioEndpointKind.Playback)
        {
            throw new ArgumentException("The Sonar microphone endpoint must be a playback device.", nameof(sonarMicrophoneEndpoint));
        }

        var targets = DiscordProcessMatcher.NormalizeTargets(targetProcessNames);
        var playbackResult = audioService.SetDiscordVolumeToZero(playbackEndpoint, targets);
        var sonarMicrophoneResult = audioService.SetDiscordVolumeToZero(sonarMicrophoneEndpoint, targets);

        return new MuteDiscordResult(playbackResult, sonarMicrophoneResult);
    }
}

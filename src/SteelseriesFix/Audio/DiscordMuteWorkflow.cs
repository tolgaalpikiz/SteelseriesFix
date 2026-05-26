namespace SteelseriesFix.Audio;

public sealed class DiscordMuteWorkflow(IAudioService audioService)
{
    public MuteDiscordResult Apply(AudioEndpoint playbackEndpoint, AudioEndpoint captureEndpoint, IEnumerable<string>? targetProcessNames)
    {
        if (playbackEndpoint.Kind != AudioEndpointKind.Playback)
        {
            throw new ArgumentException("The playback endpoint must be a playback device.", nameof(playbackEndpoint));
        }

        if (captureEndpoint.Kind != AudioEndpointKind.Capture)
        {
            throw new ArgumentException("The capture endpoint must be a capture device.", nameof(captureEndpoint));
        }

        var targets = DiscordProcessMatcher.NormalizeTargets(targetProcessNames);
        var playbackResult = audioService.SetDiscordVolumeToZero(playbackEndpoint, targets);
        var captureResult = audioService.SetDiscordVolumeToZero(captureEndpoint, targets);

        return new MuteDiscordResult(playbackResult, captureResult);
    }
}

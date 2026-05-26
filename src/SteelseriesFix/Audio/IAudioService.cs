namespace SteelseriesFix.Audio;

public interface IAudioService
{
    IReadOnlyList<AudioEndpoint> GetEndpoints(AudioEndpointKind kind);

    EndpointMuteResult SetDiscordVolumeToZero(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames);
}

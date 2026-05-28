namespace SteelseriesFix.Audio;

public interface IAudioService
{
    IReadOnlyList<AudioEndpoint> GetEndpoints(AudioEndpointKind kind);

    IReadOnlyList<AudioSessionInfo> GetSessions(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames);

    EndpointMuteResult SetDiscordVolumeToZero(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames);
}

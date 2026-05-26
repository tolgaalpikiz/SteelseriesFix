namespace SteelseriesFix.Audio;

public sealed record EndpointMuteResult(
    AudioEndpointKind Kind,
    string EndpointId,
    string EndpointName,
    bool DeviceFound,
    int MatchedSessions,
    int UpdatedSessions,
    string? Error = null)
{
    public bool Success => DeviceFound && UpdatedSessions > 0 && string.IsNullOrWhiteSpace(Error);

    public bool DiscordMissing => DeviceFound && MatchedSessions == 0 && string.IsNullOrWhiteSpace(Error);

    public static EndpointMuteResult MissingDevice(AudioEndpoint endpoint) =>
        new(endpoint.Kind, endpoint.Id, endpoint.DisplayName, false, 0, 0);
}

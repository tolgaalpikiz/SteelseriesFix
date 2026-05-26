namespace SteelseriesFix.Audio;

public enum AudioEndpointKind
{
    Playback,
    Capture
}

public sealed record AudioEndpoint(string Id, string DisplayName, AudioEndpointKind Kind)
{
    public override string ToString() => DisplayName;
}

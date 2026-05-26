namespace SteelseriesFix.Audio;

public sealed record AudioSessionInfo(
    string EndpointId,
    string EndpointName,
    AudioEndpointKind EndpointKind,
    uint ProcessId,
    string? ProcessName,
    string? DisplayName,
    float? MasterVolume,
    bool IsTargetDiscord);

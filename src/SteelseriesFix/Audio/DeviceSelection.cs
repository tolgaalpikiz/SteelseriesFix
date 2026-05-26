namespace SteelseriesFix.Audio;

public static class DeviceSelection
{
    public static AudioEndpoint? SelectSavedOrFirst(string? savedEndpointId, IReadOnlyList<AudioEndpoint> endpoints)
    {
        return SelectSavedOrPreferredOrFirst(savedEndpointId, endpoints, _ => false);
    }

    public static AudioEndpoint? SelectSavedOrPreferredOrFirst(
        string? savedEndpointId,
        IReadOnlyList<AudioEndpoint> endpoints,
        Func<AudioEndpoint, bool> isPreferredEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(savedEndpointId))
        {
            var savedEndpoint = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, savedEndpointId, StringComparison.OrdinalIgnoreCase));

            if (savedEndpoint is not null)
            {
                return savedEndpoint;
            }
        }

        var preferredEndpoint = endpoints.FirstOrDefault(isPreferredEndpoint);
        if (preferredEndpoint is not null)
        {
            return preferredEndpoint;
        }

        return endpoints.FirstOrDefault();
    }
}

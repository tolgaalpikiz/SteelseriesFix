namespace SteelseriesFix.Audio;

public static class DeviceSelection
{
    public static AudioEndpoint? SelectSavedOrFirst(string? savedEndpointId, IReadOnlyList<AudioEndpoint> endpoints)
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

        return endpoints.FirstOrDefault();
    }
}

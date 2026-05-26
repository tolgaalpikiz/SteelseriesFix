namespace SteelseriesFix.Audio;

public sealed record MuteDiscordResult(EndpointMuteResult Playback, EndpointMuteResult SonarMicrophone)
{
    public bool Success => Playback.Success && SonarMicrophone.Success;

    public string ToStatusMessage()
    {
        if (Success)
        {
            return $"Discord volume set to 0 on both mixer devices. Headphones sessions updated: {Playback.UpdatedSessions}. Sonar microphone sessions updated: {SonarMicrophone.UpdatedSessions}.";
        }

        var messages = new List<string>();
        AddEndpointMessage(messages, Playback, "headphones/playback mixer");
        AddEndpointMessage(messages, SonarMicrophone, "Sonar microphone mixer");

        return messages.Count == 0
            ? "No Discord sessions were updated."
            : string.Join(Environment.NewLine, messages);
    }

    private static void AddEndpointMessage(List<string> messages, EndpointMuteResult result, string label)
    {
        if (!result.DeviceFound)
        {
            messages.Add($"The selected {label} device was not found: {result.EndpointName}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            messages.Add($"Could not update Discord on the selected {label} device ({result.EndpointName}): {result.Error}");
            return;
        }

        if (result.DiscordMissing)
        {
            messages.Add($"Discord was not found on the selected {label} device: {result.EndpointName}");
            return;
        }

        if (result.UpdatedSessions == 0)
        {
            messages.Add($"No Discord sessions were updated on the selected {label} device: {result.EndpointName}");
        }
    }
}

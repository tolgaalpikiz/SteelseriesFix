using SteelseriesFix.Audio;
using SteelseriesFix.Settings;

var service = new CoreAudioService();

if (args.Contains("--apply-saved", StringComparer.OrdinalIgnoreCase))
{
    ApplySaved();
    return;
}

Print(AudioEndpointKind.Playback, includeSessions: true);
Print(AudioEndpointKind.Capture, includeSessions: true);

void ApplySaved()
{
    var settings = SettingsStore.CreateDefault().Load();
    var playback = service.GetEndpoints(AudioEndpointKind.Playback)
        .FirstOrDefault(endpoint => string.Equals(endpoint.Id, settings.PlaybackEndpointId, StringComparison.OrdinalIgnoreCase));
    var capture = service.GetEndpoints(AudioEndpointKind.Capture)
        .FirstOrDefault(endpoint => string.Equals(endpoint.Id, settings.CaptureEndpointId, StringComparison.OrdinalIgnoreCase));

    if (playback is null || capture is null)
    {
        Console.WriteLine("Saved playback or capture endpoint was not found.");
        return;
    }

    Console.WriteLine($"Playback: {playback.DisplayName}");
    Console.WriteLine($"Capture: {capture.DisplayName}");
    Console.WriteLine();

    PrintDiscordSessions("Before", playback, capture, settings.TargetProcessNames);

    var result = new DiscordMuteWorkflow(service).Apply(playback, capture, settings.TargetProcessNames);
    Console.WriteLine(result.ToStatusMessage());
    Console.WriteLine();

    PrintDiscordSessions("After", playback, capture, settings.TargetProcessNames);
}

void PrintDiscordSessions(string label, AudioEndpoint playback, AudioEndpoint capture, IReadOnlyCollection<string> targetProcessNames)
{
    Console.WriteLine(label);
    foreach (var endpoint in new[] { playback, capture })
    {
        var sessions = service.GetSessions(endpoint, targetProcessNames)
            .Where(session => session.IsTargetDiscord)
            .ToArray();

        Console.WriteLine($"- {endpoint.DisplayName}: {sessions.Length} Discord session(s)");
        foreach (var session in sessions)
        {
            Console.WriteLine($"  PID: {session.ProcessId}, Volume: {FormatVolume(session.MasterVolume)}");
        }
    }

    Console.WriteLine();
}

void Print(AudioEndpointKind kind, bool includeSessions)
{
    Console.WriteLine(kind);

    try
    {
        var endpoints = service.GetEndpoints(kind);
        Console.WriteLine($"Count: {endpoints.Count}");

        foreach (var endpoint in endpoints)
        {
            Console.WriteLine($"- Name: '{endpoint.DisplayName}'");
            Console.WriteLine($"  Id: {endpoint.Id}");

            if (!includeSessions)
            {
                continue;
            }

            var sessions = service.GetSessions(endpoint, DiscordProcessMatcher.DefaultProcessNames);
            Console.WriteLine($"  Sessions: {sessions.Count}");
            foreach (var session in sessions)
            {
                Console.WriteLine($"    - PID: {session.ProcessId}, Process: '{session.ProcessName ?? "<unknown>"}', Display: '{session.DisplayName ?? "<none>"}', Volume: {FormatVolume(session.MasterVolume)}, Discord: {session.IsTargetDiscord}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex}");
    }

    Console.WriteLine();
}

static string FormatVolume(float? volume) => volume is null ? "<unavailable>" : volume.Value.ToString("0.00");

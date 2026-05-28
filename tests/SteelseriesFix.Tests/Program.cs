using SteelseriesFix.Audio;
using SteelseriesFix.Settings;

var tests = new (string Name, Action Body)[]
{
    ("Settings store saves and reloads selected endpoint IDs", SettingsStoreSavesAndReloadsSelections),
    ("Device selection restores by endpoint ID", DeviceSelectionRestoresByEndpointId),
    ("Device selection falls back when saved endpoint is missing", DeviceSelectionFallsBackWhenSavedEndpointIsMissing),
    ("Device selection prefers Sonar microphone playback endpoint", DeviceSelectionPrefersSonarMicrophonePlaybackEndpoint),
    ("Discord matcher handles exe names and process names", DiscordMatcherHandlesExeNamesAndProcessNames),
    ("Mute workflow succeeds only when both endpoints update Discord", MuteWorkflowSucceedsWhenBothEndpointsUpdateDiscord),
    ("Mute workflow reports missing Discord sessions", MuteWorkflowReportsMissingDiscordSessions),
    ("Volume monitor only applies when Discord volume is high", VolumeMonitorAppliesOnlyWhenDiscordVolumeIsHigh)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

if (failed > 0)
{
    Console.WriteLine($"{failed} test(s) failed.");
    Environment.Exit(1);
}

Console.WriteLine($"{tests.Length} tests passed.");

static void SettingsStoreSavesAndReloadsSelections()
{
    var settingsPath = Path.Combine(Path.GetTempPath(), "SteelseriesFix.Tests", Guid.NewGuid().ToString("N"), "settings.json");
    var store = new SettingsStore(settingsPath);

    store.Save(new AppSettings
    {
        PlaybackEndpointId = "playback-id",
        SonarMicrophonePlaybackEndpointId = "sonar-mic-playback-id",
        TargetProcessNames = ["Discord"]
    });

    var loaded = store.Load();

    Assert.Equal("playback-id", loaded.PlaybackEndpointId);
    Assert.Equal("sonar-mic-playback-id", loaded.SonarMicrophonePlaybackEndpointId);
    Assert.True(loaded.TargetProcessNames.Contains("Discord.exe", StringComparer.OrdinalIgnoreCase));
}

static void DeviceSelectionRestoresByEndpointId()
{
    var endpoints = new[]
    {
        new AudioEndpoint("first", "First", AudioEndpointKind.Playback),
        new AudioEndpoint("saved", "Saved", AudioEndpointKind.Playback)
    };

    var selected = DeviceSelection.SelectSavedOrFirst("saved", endpoints);

    Assert.Equal("saved", selected?.Id);
}

static void DeviceSelectionFallsBackWhenSavedEndpointIsMissing()
{
    var endpoints = new[]
    {
        new AudioEndpoint("first", "First", AudioEndpointKind.Capture),
        new AudioEndpoint("second", "Second", AudioEndpointKind.Capture)
    };

    var selected = DeviceSelection.SelectSavedOrFirst("missing", endpoints);

    Assert.Equal("first", selected?.Id);
}

static void DeviceSelectionPrefersSonarMicrophonePlaybackEndpoint()
{
    var endpoints = new[]
    {
        new AudioEndpoint("headphones", "Headphones", AudioEndpointKind.Playback),
        new AudioEndpoint("sonar-mic", "SteelSeries Sonar - Microphone (SteelSeries Sonar Virtual Audio Device)", AudioEndpointKind.Playback)
    };

    var selected = DeviceSelection.SelectSavedOrPreferredOrFirst(
        "old-capture-id",
        endpoints,
        endpoint => endpoint.DisplayName.Contains("Sonar", StringComparison.OrdinalIgnoreCase) &&
                    endpoint.DisplayName.Contains("Microphone", StringComparison.OrdinalIgnoreCase));

    Assert.Equal("sonar-mic", selected?.Id);
}

static void DiscordMatcherHandlesExeNamesAndProcessNames()
{
    Assert.True(DiscordProcessMatcher.IsTargetProcess("Discord", DiscordProcessMatcher.DefaultProcessNames));
    Assert.True(DiscordProcessMatcher.IsTargetProcess("DiscordCanary.exe", DiscordProcessMatcher.DefaultProcessNames));
    Assert.True(DiscordProcessMatcher.IsTargetProcess(@"C:\Users\user\AppData\Local\DiscordPTB\DiscordPTB.exe", DiscordProcessMatcher.DefaultProcessNames));
    Assert.False(DiscordProcessMatcher.IsTargetProcess("not-discord.exe", DiscordProcessMatcher.DefaultProcessNames));
}

static void MuteWorkflowSucceedsWhenBothEndpointsUpdateDiscord()
{
    var playback = new AudioEndpoint("playback", "Headphones", AudioEndpointKind.Playback);
    var sonarMicrophone = new AudioEndpoint("sonar-microphone", "SteelSeries Sonar - Microphone", AudioEndpointKind.Playback);
    var audioService = new FakeAudioService();
    audioService.Results[playback.Id] = new EndpointMuteResult(playback.Kind, playback.Id, playback.DisplayName, true, 1, 1);
    audioService.Results[sonarMicrophone.Id] = new EndpointMuteResult(sonarMicrophone.Kind, sonarMicrophone.Id, sonarMicrophone.DisplayName, true, 1, 1);

    var result = new DiscordMuteWorkflow(audioService).Apply(playback, sonarMicrophone, DiscordProcessMatcher.DefaultProcessNames);

    Assert.True(result.Success);
}

static void MuteWorkflowReportsMissingDiscordSessions()
{
    var playback = new AudioEndpoint("playback", "Headphones", AudioEndpointKind.Playback);
    var sonarMicrophone = new AudioEndpoint("sonar-microphone", "SteelSeries Sonar - Microphone", AudioEndpointKind.Playback);
    var audioService = new FakeAudioService();
    audioService.Results[playback.Id] = new EndpointMuteResult(playback.Kind, playback.Id, playback.DisplayName, true, 1, 1);
    audioService.Results[sonarMicrophone.Id] = new EndpointMuteResult(sonarMicrophone.Kind, sonarMicrophone.Id, sonarMicrophone.DisplayName, true, 0, 0);

    var result = new DiscordMuteWorkflow(audioService).Apply(playback, sonarMicrophone, DiscordProcessMatcher.DefaultProcessNames);

    Assert.False(result.Success);
    Assert.True(result.ToStatusMessage().Contains("Discord was not found", StringComparison.OrdinalIgnoreCase));
}

static void VolumeMonitorAppliesOnlyWhenDiscordVolumeIsHigh()
{
    var playback = new AudioEndpoint("playback", "Headphones", AudioEndpointKind.Playback);
    var sonarMicrophone = new AudioEndpoint("sonar-microphone", "SteelSeries Sonar - Microphone", AudioEndpointKind.Playback);
    var audioService = new FakeAudioService();
    audioService.Endpoints.Add(playback);
    audioService.Endpoints.Add(sonarMicrophone);
    audioService.Sessions[playback.Id] =
    [
        new AudioSessionInfo(playback.Id, playback.DisplayName, playback.Kind, 10, "Discord", null, 0.0f, true)
    ];
    audioService.Sessions[sonarMicrophone.Id] =
    [
        new AudioSessionInfo(sonarMicrophone.Id, sonarMicrophone.DisplayName, sonarMicrophone.Kind, 11, "Discord", null, 0.8f, true)
    ];
    audioService.Results[sonarMicrophone.Id] = new EndpointMuteResult(sonarMicrophone.Kind, sonarMicrophone.Id, sonarMicrophone.DisplayName, true, 1, 1);

    var result = new DiscordVolumeMonitor(audioService).CheckAndFix(new AppSettings
    {
        PlaybackEndpointId = playback.Id,
        SonarMicrophonePlaybackEndpointId = sonarMicrophone.Id,
        TargetProcessNames = ["Discord.exe"],
        MonitorVolumeThreshold = 0.001f
    });

    Assert.True(result.ChangedVolume);
    Assert.Equal(1, result.HighSessions);
    Assert.Equal(1, result.UpdatedSessions);
    Assert.Equal(1, audioService.ApplyCalls);
}

sealed class FakeAudioService : IAudioService
{
    public List<AudioEndpoint> Endpoints { get; } = [];

    public Dictionary<string, IReadOnlyList<AudioSessionInfo>> Sessions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, EndpointMuteResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int ApplyCalls { get; private set; }

    public IReadOnlyList<AudioEndpoint> GetEndpoints(AudioEndpointKind kind)
    {
        return Endpoints.Where(endpoint => endpoint.Kind == kind).ToArray();
    }

    public IReadOnlyList<AudioSessionInfo> GetSessions(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames)
    {
        return Sessions.TryGetValue(endpoint.Id, out var sessions) ? sessions : [];
    }

    public EndpointMuteResult SetDiscordVolumeToZero(AudioEndpoint endpoint, IReadOnlyCollection<string> targetProcessNames)
    {
        ApplyCalls++;
        return Results.TryGetValue(endpoint.Id, out var result)
            ? result
            : new EndpointMuteResult(endpoint.Kind, endpoint.Id, endpoint.DisplayName, true, 0, 0);
    }
}

static class Assert
{
    public static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected condition to be true.");
        }
    }

    public static void False(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("Expected condition to be false.");
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }
}

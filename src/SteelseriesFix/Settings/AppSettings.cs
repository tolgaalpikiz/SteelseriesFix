using SteelseriesFix.Audio;

namespace SteelseriesFix.Settings;

public sealed class AppSettings
{
    public string? PlaybackEndpointId { get; set; }

    public string? CaptureEndpointId { get; set; }

    public List<string> TargetProcessNames { get; set; } = DiscordProcessMatcher.DefaultProcessNames.ToList();

    public static AppSettings CreateDefault() => new()
    {
        TargetProcessNames = DiscordProcessMatcher.DefaultProcessNames.ToList()
    };

    public AppSettings Normalize()
    {
        TargetProcessNames = DiscordProcessMatcher.NormalizeTargets(TargetProcessNames).ToList();
        return this;
    }
}

using System.IO;

namespace SteelseriesFix.Audio;

public static class DiscordProcessMatcher
{
    public static readonly string[] DefaultProcessNames =
    [
        "Discord.exe",
        "DiscordPTB.exe",
        "DiscordCanary.exe",
        "DiscordDevelopment.exe"
    ];

    public static bool IsTargetProcess(string? processName, IEnumerable<string>? targetProcessNames)
    {
        var normalizedProcessName = NormalizeProcessName(processName);
        if (normalizedProcessName is null)
        {
            return false;
        }

        return NormalizeTargets(targetProcessNames).Contains(normalizedProcessName, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> NormalizeTargets(IEnumerable<string>? targetProcessNames)
    {
        var normalizedTargets = (targetProcessNames ?? DefaultProcessNames)
            .Select(NormalizeProcessName)
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalizedTargets.Length == 0 ? DefaultProcessNames : normalizedTargets;
    }

    public static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var fileName = Path.GetFileName(processName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".exe";
    }
}

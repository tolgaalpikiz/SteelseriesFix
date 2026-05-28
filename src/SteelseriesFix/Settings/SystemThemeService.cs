using Microsoft.Win32;

namespace SteelseriesFix.Settings;

public sealed class SystemThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    public bool IsSystemDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
        return key?.GetValue(AppsUseLightThemeValue) is int appsUseLightTheme && appsUseLightTheme == 0;
    }
}

# SteelseriesFix

Small Windows WPF tool for the SteelSeries GG Sonar + Discord screen-share echo issue.

The app lists playback mixer devices, remembers the last selected headphones and Sonar microphone mixer endpoints, and keeps Discord's per-app volume at `0` on both selected endpoints.

It runs as a tray process by default, opens the setup UI when the tray icon is clicked, hides instead of closing, and registers itself under the current user's Windows startup apps.
The UI uses the Windows app theme by default and can be switched between System, Dark, and Light from the small theme button.

## Build

```powershell
dotnet build
```

## Run

```powershell
dotnet run --project src\SteelseriesFix\SteelseriesFix.csproj
```

## Test

```powershell
dotnet run --project tests\SteelseriesFix.Tests\SteelseriesFix.Tests.csproj
```

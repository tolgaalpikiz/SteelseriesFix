# SteelseriesFix

Small Windows WPF tool for the SteelSeries GG Sonar + Discord screen-share echo issue.

The app lists active playback and capture devices, remembers the last selected devices, and sets Discord's per-app volume to `0` on both selected endpoints.

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

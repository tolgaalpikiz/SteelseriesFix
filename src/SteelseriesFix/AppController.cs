using System.IO;
using System.Security;
using System.Windows.Threading;
using SteelseriesFix.Audio;
using SteelseriesFix.Settings;
using Forms = System.Windows.Forms;

namespace SteelseriesFix;

public sealed class AppController : IDisposable
{
    private readonly IAudioService _audioService;
    private readonly DiscordMuteWorkflow _muteWorkflow;
    private readonly DiscordVolumeMonitor _volumeMonitor;
    private readonly SettingsStore _settingsStore;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly SystemThemeService _systemThemeService;
    private readonly DispatcherTimer _monitorTimer;
    private AppSettings _settings = AppSettings.CreateDefault();
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _window;
    private bool _monitorTickRunning;

    public AppController()
        : this(new CoreAudioService(), SettingsStore.CreateDefault(), new StartupRegistrationService(), new SystemThemeService())
    {
    }

    internal AppController(
        IAudioService audioService,
        SettingsStore settingsStore,
        StartupRegistrationService startupRegistrationService,
        SystemThemeService systemThemeService)
    {
        _audioService = audioService;
        _muteWorkflow = new DiscordMuteWorkflow(audioService);
        _volumeMonitor = new DiscordVolumeMonitor(audioService);
        _settingsStore = settingsStore;
        _startupRegistrationService = startupRegistrationService;
        _systemThemeService = systemThemeService;
        _monitorTimer = new DispatcherTimer();
        _monitorTimer.Tick += MonitorTimer_Tick;
    }

    public void Start()
    {
        _settings = _settingsStore.Load();
        EnsureTrayIcon();
        ConfigureStartupRegistration();
        ConfigureMonitorTimer();
        ShowNotification("SteelSeries Discord Echo Fix", "Running in the tray. Click the tray icon to open settings.");
    }

    public void Dispose()
    {
        _monitorTimer.Stop();
        _notifyIcon?.Dispose();
    }

    private void EnsureTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenWindow());
        menu.Items.Add("Mute Discord Now", null, async (_, _) => await MuteSavedDevicesAsync(showNotification: true));
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "SteelSeries Discord Echo Fix",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenWindow();
            }
        };
    }

    private void OpenWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow(_audioService, _settingsStore, _startupRegistrationService, _systemThemeService);
            _window.SettingsSaved += Window_SettingsSaved;
            _window.Closed += (_, _) =>
            {
                _window.SettingsSaved -= Window_SettingsSaved;
                _window = null;
                GC.Collect();
            };
        }

        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    private void Window_SettingsSaved(object? sender, EventArgs e)
    {
        _settings = _settingsStore.Load();
        ConfigureStartupRegistration();
        ConfigureMonitorTimer();
    }

    private async void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (_monitorTickRunning || !_settings.AutoMonitorEnabled || !HasSavedDevices(_settings))
        {
            return;
        }

        _monitorTickRunning = true;
        try
        {
            var settingsSnapshot = _settings.Normalize();
            var result = await Task.Run(() => _volumeMonitor.CheckAndFix(settingsSnapshot));
            if (result.ChangedVolume)
            {
                ShowNotification("Discord volume reset", result.Message);
            }
        }
        finally
        {
            _monitorTickRunning = false;
        }
    }

    private async Task MuteSavedDevicesAsync(bool showNotification)
    {
        _settings = _settingsStore.Load();
        if (!HasSavedDevices(_settings))
        {
            if (showNotification)
            {
                ShowNotification("Setup needed", "Open the app and save the two mixer devices first.");
            }

            return;
        }

        var targets = _settings.TargetProcessNames.ToArray();
        var playbackEndpoint = new AudioEndpoint(_settings.PlaybackEndpointId!, "Saved playback device", AudioEndpointKind.Playback);
        var sonarMicrophoneEndpoint = new AudioEndpoint(_settings.SonarMicrophonePlaybackEndpointId!, "Saved Sonar microphone mixer", AudioEndpointKind.Playback);
        var result = await Task.Run(() => _muteWorkflow.Apply(playbackEndpoint, sonarMicrophoneEndpoint, targets));

        if (showNotification)
        {
            ShowNotification(result.Success ? "Discord muted" : "Discord not found", result.ToStatusMessage());
        }
    }

    private void ConfigureStartupRegistration()
    {
        try
        {
            _startupRegistrationService.SetEnabled(_settings.RunAtStartup);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SecurityException or UnauthorizedAccessException)
        {
            ShowNotification("Startup registration failed", ex.Message);
        }
    }

    private void ConfigureMonitorTimer()
    {
        _monitorTimer.Stop();
        _monitorTimer.Interval = TimeSpan.FromSeconds(_settings.MonitorIntervalSeconds);

        if (_settings.AutoMonitorEnabled && HasSavedDevices(_settings))
        {
            _monitorTimer.Start();
        }
    }

    private static bool HasSavedDevices(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.PlaybackEndpointId) &&
               !string.IsNullOrWhiteSpace(settings.SonarMicrophonePlaybackEndpointId);
    }

    private void ShowNotification(string title, string message)
    {
        _notifyIcon?.ShowBalloonTip(2500, title, message, Forms.ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}

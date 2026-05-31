using System.IO;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SteelseriesFix.Audio;
using SteelseriesFix.Settings;

namespace SteelseriesFix;

public partial class MainWindow : Window
{
    private readonly IAudioService _audioService;
    private readonly DiscordMuteWorkflow _muteWorkflow;
    private readonly SettingsStore _settingsStore;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly SystemThemeService _systemThemeService;
    private AppSettings _settings = AppSettings.CreateDefault();
    private bool _isApplyingSettingsToControls;

    public event EventHandler? SettingsSaved;

    public MainWindow() : this(new CoreAudioService(), SettingsStore.CreateDefault(), new StartupRegistrationService(), new SystemThemeService())
    {
    }

    internal MainWindow(
        IAudioService audioService,
        SettingsStore settingsStore,
        StartupRegistrationService startupRegistrationService,
        SystemThemeService systemThemeService)
    {
        _audioService = audioService;
        _muteWorkflow = new DiscordMuteWorkflow(audioService);
        _settingsStore = settingsStore;
        _startupRegistrationService = startupRegistrationService;
        _systemThemeService = systemThemeService;

        _settings = _settingsStore.Load();
        ApplyThemeResources();
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySettingsToControls();
        UpdateThemeButton();
        RefreshDevices(restoreSavedSelection: true);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevices(restoreSavedSelection: false);
    }

    private async void MuteDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyMuteFromUiAsync(showBusy: true);
    }

    private void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isApplyingSettingsToControls)
        {
            return;
        }

        var previousRunAtStartup = _settings.RunAtStartup;
        var requestedRunAtStartup = RunAtStartupCheckBox.IsChecked == true;

        if (ReferenceEquals(sender, RunAtStartupCheckBox) &&
            requestedRunAtStartup != previousRunAtStartup &&
            !TryApplyStartupRegistration(requestedRunAtStartup, previousRunAtStartup))
        {
            return;
        }

        UpdateSettingsFromControls();
        if (SaveSettings())
        {
            if (ReferenceEquals(sender, RunAtStartupCheckBox))
            {
                SetStatus(
                    _settings.RunAtStartup
                        ? "Windows startup is enabled. The app will start in the tray after sign-in."
                        : "Windows startup is disabled.",
                    StatusKind.Success);
            }

            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ThemeMode = _settings.ThemeMode switch
        {
            ThemeMode.System => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.Light,
            _ => ThemeMode.System
        };

        ApplyThemeResources();
        UpdateThemeButton();
        if (SaveSettings())
        {
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DeviceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateApplyState();
    }

    private async Task ApplyMuteFromUiAsync(bool showBusy)
    {
        if (PlaybackDeviceComboBox.SelectedItem is not AudioEndpoint playbackEndpoint ||
            SonarMicrophoneDeviceComboBox.SelectedItem is not AudioEndpoint sonarMicrophoneEndpoint)
        {
            SetStatus("Select both audio devices before muting Discord.", StatusKind.Warning);
            return;
        }

        _settings.PlaybackEndpointId = playbackEndpoint.Id;
        _settings.SonarMicrophonePlaybackEndpointId = sonarMicrophoneEndpoint.Id;
        _settings.CaptureEndpointId = null;
        UpdateSettingsFromControls();

        if (!SaveSettings())
        {
            return;
        }

        SettingsSaved?.Invoke(this, EventArgs.Empty);

        if (showBusy)
        {
            SetBusy(true);
        }

        SetStatus("Muting Discord on the selected mixer devices...", StatusKind.Neutral);

        try
        {
            var targetProcessNames = _settings.TargetProcessNames.ToArray();
            var result = await Task.Run(() => _muteWorkflow.Apply(playbackEndpoint, sonarMicrophoneEndpoint, targetProcessNames));
            SetStatus(result.ToStatusMessage(), result.Success ? StatusKind.Success : StatusKind.Warning);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not mute Discord: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            if (showBusy)
            {
                SetBusy(false);
            }
        }
    }

    private void RefreshDevices(bool restoreSavedSelection)
    {
        SetBusy(true);

        try
        {
            var playbackSelectionId = restoreSavedSelection
                ? _settings.PlaybackEndpointId
                : (PlaybackDeviceComboBox.SelectedItem as AudioEndpoint)?.Id ?? _settings.PlaybackEndpointId;
            var sonarMicrophoneSelectionId = restoreSavedSelection
                ? _settings.SonarMicrophonePlaybackEndpointId
                : (SonarMicrophoneDeviceComboBox.SelectedItem as AudioEndpoint)?.Id ?? _settings.SonarMicrophonePlaybackEndpointId;

            var playbackEndpoints = _audioService.GetEndpoints(AudioEndpointKind.Playback);
            var sonarMicrophoneEndpoints = playbackEndpoints;

            PlaybackDeviceComboBox.ItemsSource = playbackEndpoints;
            SonarMicrophoneDeviceComboBox.ItemsSource = sonarMicrophoneEndpoints;
            PlaybackDeviceComboBox.SelectedItem = DeviceSelection.SelectSavedOrFirst(playbackSelectionId, playbackEndpoints);
            SonarMicrophoneDeviceComboBox.SelectedItem = DeviceSelection.SelectSavedOrPreferredOrFirst(
                sonarMicrophoneSelectionId,
                sonarMicrophoneEndpoints,
                IsLikelySonarMicrophonePlaybackEndpoint);

            if (playbackEndpoints.Count == 0 || sonarMicrophoneEndpoints.Count == 0)
            {
                SetStatus($"Loaded {playbackEndpoints.Count} playback mixer device(s). If the list is empty, confirm the device is enabled in Windows sound settings.", StatusKind.Warning);
            }
            else
            {
                SetStatus($"Loaded {playbackEndpoints.Count} playback mixer device(s). Select your headphones and SteelSeries Sonar - Microphone, then click Mute Discord.", StatusKind.Neutral);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load audio devices: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateApplyState();
        }
    }

    private void ApplySettingsToControls()
    {
        _isApplyingSettingsToControls = true;
        try
        {
            AutoMonitorCheckBox.IsChecked = _settings.AutoMonitorEnabled;
            RunAtStartupCheckBox.IsChecked = _settings.RunAtStartup;
        }
        finally
        {
            _isApplyingSettingsToControls = false;
        }
    }

    private void UpdateThemeButton()
    {
        ThemeButton.Content = _settings.ThemeMode.ToString();
        ThemeButton.ToolTip = $"Theme: {_settings.ThemeMode}. Click to cycle.";
    }

    private void UpdateSettingsFromControls()
    {
        _settings.AutoMonitorEnabled = AutoMonitorCheckBox.IsChecked == true;
        _settings.RunAtStartup = RunAtStartupCheckBox.IsChecked == true;
    }

    private bool SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not save settings: {ex.Message}", StatusKind.Error);
            return false;
        }
    }

    private bool TryApplyStartupRegistration(bool enabled, bool previousValue)
    {
        try
        {
            _startupRegistrationService.SetEnabled(enabled);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SecurityException or UnauthorizedAccessException)
        {
            SetStatus($"Could not update Windows startup: {ex.Message}", StatusKind.Error);
            _isApplyingSettingsToControls = true;
            try
            {
                RunAtStartupCheckBox.IsChecked = previousValue;
            }
            finally
            {
                _isApplyingSettingsToControls = false;
            }

            return false;
        }
    }

    private void SetBusy(bool isBusy)
    {
        PlaybackDeviceComboBox.IsEnabled = !isBusy;
        SonarMicrophoneDeviceComboBox.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        MuteDiscordButton.IsEnabled = !isBusy;
        AutoMonitorCheckBox.IsEnabled = !isBusy;
        RunAtStartupCheckBox.IsEnabled = !isBusy;
    }

    private void UpdateApplyState()
    {
        MuteDiscordButton.IsEnabled =
            PlaybackDeviceComboBox.SelectedItem is AudioEndpoint &&
            SonarMicrophoneDeviceComboBox.SelectedItem is AudioEndpoint &&
            PlaybackDeviceComboBox.IsEnabled &&
            SonarMicrophoneDeviceComboBox.IsEnabled;
    }

    private static bool IsLikelySonarMicrophonePlaybackEndpoint(AudioEndpoint endpoint)
    {
        return endpoint.Kind == AudioEndpointKind.Playback &&
               endpoint.DisplayName.Contains("SteelSeries Sonar", StringComparison.OrdinalIgnoreCase) &&
               endpoint.DisplayName.Contains("Microphone", StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = kind switch
        {
            StatusKind.Success => new SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 127, 55)),
            StatusKind.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 103, 0)),
            StatusKind.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(207, 34, 46)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(87, 96, 106))
        };
    }

    private void ApplyThemeResources()
    {
        var useDarkTheme = _settings.ThemeMode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => _systemThemeService.IsSystemDarkMode()
        };

        var resources = System.Windows.Application.Current.Resources;
        resources["AppBackgroundBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(20, 23, 27)
            : System.Windows.Media.Color.FromRgb(246, 247, 249));
        resources["PanelBackgroundBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(31, 35, 40)
            : System.Windows.Media.Color.FromRgb(255, 255, 255));
        resources["ControlBackgroundBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(39, 44, 51)
            : System.Windows.Media.Color.FromRgb(255, 255, 255));
        resources["BorderBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(65, 72, 82)
            : System.Windows.Media.Color.FromRgb(208, 215, 222));
        resources["PrimaryTextBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(239, 246, 252)
            : System.Windows.Media.Color.FromRgb(31, 35, 40));
        resources["SecondaryTextBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(207, 216, 226)
            : System.Windows.Media.Color.FromRgb(52, 57, 65));
        resources["MutedTextBrush"] = new SolidColorBrush(useDarkTheme
            ? System.Windows.Media.Color.FromRgb(173, 186, 199)
            : System.Windows.Media.Color.FromRgb(87, 96, 106));
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Warning,
        Error
    }
}

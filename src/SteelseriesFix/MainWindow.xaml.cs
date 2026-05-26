using System.IO;
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
    private AppSettings _settings = AppSettings.CreateDefault();

    public MainWindow() : this(new CoreAudioService(), SettingsStore.CreateDefault())
    {
    }

    internal MainWindow(IAudioService audioService, SettingsStore settingsStore)
    {
        _audioService = audioService;
        _muteWorkflow = new DiscordMuteWorkflow(audioService);
        _settingsStore = settingsStore;

        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        RefreshDevices(restoreSavedSelection: true);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevices(restoreSavedSelection: false);
    }

    private async void MuteDiscordButton_Click(object sender, RoutedEventArgs e)
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

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not save selected devices: {ex.Message}", StatusKind.Error);
            return;
        }

        SetBusy(true);
        SetStatus("Muting Discord on the selected devices...", StatusKind.Neutral);

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
            SetBusy(false);
        }
    }

    private void DeviceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateApplyState();
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

    private void SetBusy(bool isBusy)
    {
        PlaybackDeviceComboBox.IsEnabled = !isBusy;
        SonarMicrophoneDeviceComboBox.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        MuteDiscordButton.IsEnabled = !isBusy;
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
            StatusKind.Success => new SolidColorBrush(Color.FromRgb(26, 127, 55)),
            StatusKind.Warning => new SolidColorBrush(Color.FromRgb(154, 103, 0)),
            StatusKind.Error => new SolidColorBrush(Color.FromRgb(207, 34, 46)),
            _ => new SolidColorBrush(Color.FromRgb(87, 96, 106))
        };
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Warning,
        Error
    }
}

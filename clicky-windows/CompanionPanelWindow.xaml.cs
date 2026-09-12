using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Clicky;

/// <summary>
/// Code-behind for the floating companion panel.
/// Observes CompanionManager state changes and updates the UI accordingly.
/// Mirrors CompanionPanelView.swift in responsibility, but uses WPF patterns.
/// </summary>
public partial class CompanionPanelWindow : Window
{
    private readonly CompanionManager _companionManager;

    // Number of waveform bars rendered in the WaveformCanvas
    private const int WaveformBarCount = 24;
    private readonly List<Rectangle> _waveformBars = new();

    public CompanionPanelWindow(CompanionManager companionManager)
    {
        _companionManager = companionManager;
        InitializeComponent();
        InitializeWaveformBars();
        SubscribeToCompanionManagerEvents();
    }

    // ── Initialization ────────────────────────────────────────────────────────

    private void InitializeWaveformBars()
    {
        var canvasWidth = 312.0; // WaveformCanvas width (panel width minus padding)
        var barWidth = 4.0;
        var barSpacing = (canvasWidth - barWidth * WaveformBarCount) / (WaveformBarCount - 1);

        for (int barIndex = 0; barIndex < WaveformBarCount; barIndex++)
        {
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = 4,
                Fill = DS.Colors.WaveformIdleBrush,
                RadiusX = 2,
                RadiusY = 2
            };

            System.Windows.Controls.Canvas.SetLeft(bar, barIndex * (barWidth + barSpacing));
            System.Windows.Controls.Canvas.SetTop(bar, 18); // centered in 40px canvas
            _waveformBars.Add(bar);
            WaveformCanvas.Children.Add(bar);
        }
    }

    private void SubscribeToCompanionManagerEvents()
    {
        _companionManager.VoiceStateChanged += OnVoiceStateChanged;
        _companionManager.AudioLevelChanged += OnAudioLevelChanged;
        _companionManager.PermissionsChanged += OnPermissionsChanged;
    }

    // ── CompanionManager event handlers ──────────────────────────────────────

    private void OnVoiceStateChanged(object? sender, VoiceState newVoiceState)
    {
        Dispatcher.Invoke(() => UpdateUiForVoiceState(newVoiceState));
    }

    private void OnAudioLevelChanged(object? sender, float audioLevel)
    {
        Dispatcher.Invoke(() => UpdateWaveformBars(audioLevel));
    }

    private void OnPermissionsChanged(object? sender, PermissionsSnapshot permissionsSnapshot)
    {
        Dispatcher.Invoke(() => UpdatePermissionDots(permissionsSnapshot));
    }

    // ── UI update helpers ─────────────────────────────────────────────────────

    private void UpdateUiForVoiceState(VoiceState voiceState)
    {
        switch (voiceState)
        {
            case VoiceState.Idle:
                StatusLabel.Text = "Ready";
                StatusDetailLabel.Text = "Press Ctrl + Alt to start talking";
                StatusDot.Fill = DS.Colors.AccentBrush;
                WaveformContainer.Visibility = Visibility.Collapsed;
                break;

            case VoiceState.Listening:
                StatusLabel.Text = "Listening…";
                StatusDetailLabel.Text = "Release Ctrl + Alt to send";
                StatusDot.Fill = new SolidColorBrush(DS.Colors.RecordingRed);
                WaveformContainer.Visibility = Visibility.Visible;
                break;

            case VoiceState.Processing:
                StatusLabel.Text = "Thinking…";
                StatusDetailLabel.Text = "Claude is processing your request";
                StatusDot.Fill = DS.Colors.AccentBrush;
                WaveformContainer.Visibility = Visibility.Collapsed;
                break;

            case VoiceState.Responding:
                StatusLabel.Text = "Responding";
                StatusDetailLabel.Text = "Claude is speaking";
                StatusDot.Fill = DS.Colors.AccentBrush;
                WaveformContainer.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void UpdateWaveformBars(float audioLevel)
    {
        // audioLevel is normalized 0.0–1.0 from the audio engine.
        // Each bar gets a slightly randomized height so the visualizer looks organic.
        var random = new Random();
        var canvasHeight = 40.0;

        for (int barIndex = 0; barIndex < _waveformBars.Count; barIndex++)
        {
            var bar = _waveformBars[barIndex];
            var jitter = 0.6 + random.NextDouble() * 0.8;
            var barHeight = Math.Max(4, audioLevel * canvasHeight * jitter);

            bar.Height = barHeight;
            bar.Fill = audioLevel > 0.05f ? DS.Colors.WaveformActiveBrush : DS.Colors.WaveformIdleBrush;

            // Center the bar vertically in the canvas
            System.Windows.Controls.Canvas.SetTop(bar, (canvasHeight - barHeight) / 2);
        }
    }

    private void UpdatePermissionDots(PermissionsSnapshot permissionsSnapshot)
    {
        MicPermissionDot.Fill = permissionsSnapshot.HasMicrophoneAccess
            ? new SolidColorBrush(Color.FromRgb(52, 199, 89))   // green
            : new SolidColorBrush(DS.Colors.RecordingRed);

        ScreenPermissionDot.Fill = permissionsSnapshot.HasScreenCaptureAccess
            ? new SolidColorBrush(Color.FromRgb(52, 199, 89))
            : new SolidColorBrush(DS.Colors.RecordingRed);
    }

    // ── User interaction handlers ─────────────────────────────────────────────

    private void OnModelPickerSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (ModelPickerComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
        {
            var selectedModelId = selectedItem.Tag as string ?? AppConfig.DefaultClaudeModel;
            _companionManager.SetClaudeModel(selectedModelId);
        }
    }

    private void OnShowCursorToggleChanged(object sender, RoutedEventArgs routedEventArgs)
    {
        var shouldShowCursor = ShowCursorToggle.IsChecked ?? true;
        _companionManager.SetCursorOverlayVisible(shouldShowCursor);
    }

    private void OnQuitButtonClick(object sender, RoutedEventArgs routedEventArgs)
    {
        System.Windows.Application.Current.Shutdown();
    }

    // ── Window cleanup ────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs closedEventArgs)
    {
        _companionManager.VoiceStateChanged -= OnVoiceStateChanged;
        _companionManager.AudioLevelChanged -= OnAudioLevelChanged;
        _companionManager.PermissionsChanged -= OnPermissionsChanged;
        base.OnClosed(closedEventArgs);
    }
}

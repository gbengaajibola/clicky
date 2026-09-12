using System.Windows;

namespace Clicky;

// ── Voice state machine ───────────────────────────────────────────────────────

public enum VoiceState
{
    Idle,
    Listening,
    Processing,
    Responding
}

// ── Supporting data types ─────────────────────────────────────────────────────

public record PermissionsSnapshot(bool HasMicrophoneAccess, bool HasScreenCaptureAccess);

/// <summary>
/// Central state machine.  Owns the full push-to-talk → screenshot → Claude → TTS → pointing
/// pipeline.  Mirrors CompanionManager.swift.
///
/// Collaborator responsibilities:
/// - GlobalPushToTalkShortcutMonitor: detects Ctrl+Alt key events system-wide
/// - AudioCaptureEngine: microphone capture + PCM16 conversion
/// - AssemblyAiStreamingTranscriptionProvider: streams audio to AssemblyAI, delivers transcript
/// - ScreenCaptureUtility: captures all monitors as JPEG
/// - ClaudeApiClient: SSE streaming chat with vision
/// - ElevenLabsTtsClient: TTS playback
/// - OverlayWindow: blue cursor, response text, spinner, pointing animation
/// </summary>
public sealed class CompanionManager : IDisposable
{
    // ── Events the UI observes ────────────────────────────────────────────────

    public event EventHandler<VoiceState>? VoiceStateChanged;
    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<PermissionsSnapshot>? PermissionsChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    private VoiceState _currentVoiceState = VoiceState.Idle;
    private string _currentClaudeModel = AppConfig.DefaultClaudeModel;
    private bool _isCursorOverlayVisible = true;

    private readonly List<ConversationMessage> _conversationHistory = new();

    // ── Collaborators ─────────────────────────────────────────────────────────

    private readonly ClaudeApiClient _claudeApiClient = new();
    private readonly ElevenLabsTtsClient _ttsClient = new();
    private readonly AudioCaptureEngine _audioCaptureEngine = new();
    private readonly AssemblyAiStreamingTranscriptionProvider _transcriptionProvider = new();
    private readonly GlobalPushToTalkShortcutMonitor _pushToTalkMonitor = new();

    // Created lazily on the WPF dispatcher when first needed
    private OverlayWindow? _overlayWindow;

    private CancellationTokenSource? _activeTurnCancellationSource;

    // ── Startup ───────────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        WireAudioCaptureEvents();
        WireTranscriptionProviderEvents();
        WirePushToTalkMonitorEvents();

        _pushToTalkMonitor.Start();

        _ttsClient.PlaybackStopped += OnTtsPlaybackStopped;

        await CheckAndReportPermissions();
    }

    // ── Public API called by UI ───────────────────────────────────────────────

    public void SetClaudeModel(string claudeModelId)
    {
        _currentClaudeModel = claudeModelId;
    }

    public void SetCursorOverlayVisible(bool isVisible)
    {
        _isCursorOverlayVisible = isVisible;

        if (!isVisible)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                _overlayWindow?.Hide());
        }
    }

    // ── Push-to-talk lifecycle ────────────────────────────────────────────────

    private void OnPushToTalkPressed(object? sender, EventArgs eventArgs)
    {
        if (_currentVoiceState != VoiceState.Idle) return;

        TransitionToVoiceState(VoiceState.Listening);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            EnsureOverlayWindowCreated();

            if (_isCursorOverlayVisible)
                _overlayWindow!.ShowOverlay(isTransientMode: false);
            else
                _overlayWindow!.ShowOverlay(isTransientMode: true);

            _overlayWindow!.ShowCursorAtCurrentMousePosition();
            _overlayWindow!.ClearResponseText();
        });

        _audioCaptureEngine.StartCapture();

        // Start the AssemblyAI session asynchronously — fire-and-forget; errors are handled inside
        _ = StartTranscriptionSessionAsync();
    }

    private void OnPushToTalkReleased(object? sender, EventArgs eventArgs)
    {
        if (_currentVoiceState != VoiceState.Listening) return;

        // Stop mic capture; finalize the AssemblyAI turn
        _audioCaptureEngine.StopCapture();

        TransitionToVoiceState(VoiceState.Processing);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _overlayWindow?.HideSpinner();
            _overlayWindow?.ShowSpinner();
        });

        // The transcription provider will raise TranscriptFinalized once AssemblyAI
        // delivers the session_termination message.  We signal end-of-turn here.
        _ = FinalizeTranscriptionSessionAsync();
    }

    private async Task StartTranscriptionSessionAsync()
    {
        try
        {
            await _transcriptionProvider.StartSessionAsync();
        }
        catch (Exception)
        {
            // Token fetch or WebSocket connection failed — return to idle
            TransitionToVoiceState(VoiceState.Idle);
        }
    }

    private async Task FinalizeTranscriptionSessionAsync()
    {
        try
        {
            await _transcriptionProvider.FinalizeCurrentTurnAsync();
        }
        catch (Exception)
        {
            TransitionToVoiceState(VoiceState.Idle);
        }
    }

    // ── Transcription provider events ─────────────────────────────────────────

    private void OnTranscriptFinalized(object? sender, string finalizedTranscript)
    {
        // Fired on a background thread by the WebSocket receive loop
        _ = RunConversationTurnAsync(finalizedTranscript);
    }

    private void OnTranscriptPartialUpdate(object? sender, string partialTranscript)
    {
        // Show live transcript in the overlay bubble while the user is still speaking
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _overlayWindow?.ClearResponseText();
            _overlayWindow?.AppendResponseToken(partialTranscript);
        });
    }

    // ── Audio capture events ──────────────────────────────────────────────────

    private void OnAudioChunkAvailable(object? sender, AudioChunkEventArgs audioChunkEventArgs)
    {
        // Fire-and-forget; errors logged inside provider
        _ = _transcriptionProvider.SendAudioChunkAsync(audioChunkEventArgs.Pcm16Data);
    }

    private void OnAudioLevelUpdated(object? sender, float normalizedAudioLevel)
    {
        AudioLevelChanged?.Invoke(this, normalizedAudioLevel);
    }

    // ── Conversation turn pipeline ────────────────────────────────────────────

    private async Task RunConversationTurnAsync(string userTranscript)
    {
        if (string.IsNullOrWhiteSpace(userTranscript))
        {
            TransitionToVoiceState(VoiceState.Idle);
            return;
        }

        _conversationHistory.Add(new ConversationMessage("user", userTranscript));

        _activeTurnCancellationSource?.Cancel();
        _activeTurnCancellationSource = new CancellationTokenSource();
        var cancellationToken = _activeTurnCancellationSource.Token;

        try
        {
            // Capture all screens now, just before calling Claude
            var screenCaptures = ScreenCaptureUtility.CaptureAllMonitors();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _overlayWindow?.HideSpinner();
                _overlayWindow?.ClearResponseText();
            });

            TransitionToVoiceState(VoiceState.Responding);

            var fullResponseBuilder = new System.Text.StringBuilder();

            await foreach (var token in _claudeApiClient.StreamResponseAsync(
                _conversationHistory, screenCaptures, _currentClaudeModel, cancellationToken))
            {
                fullResponseBuilder.Append(token);

                // Stream visible text (without tag syntax) to the overlay bubble
                var displayText = PointingTagParser.StripPointingTags(fullResponseBuilder.ToString());
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _overlayWindow?.ClearResponseText();
                    _overlayWindow?.AppendResponseToken(displayText);
                });
            }

            var completeResponseText = fullResponseBuilder.ToString();
            _conversationHistory.Add(new ConversationMessage("assistant", completeResponseText));

            // Animate cursor to each pointing target Claude embedded in its response
            var pointingTargets = PointingTagParser.ExtractPointingTargets(completeResponseText);
            foreach (var pointingTarget in pointingTargets)
            {
                if (cancellationToken.IsCancellationRequested) break;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_overlayWindow != null)
                        await _overlayWindow.AnimateCursorToPointingTargetAsync(pointingTarget, cancellationToken);
                });
            }

            // Speak the clean response text (tags stripped) via ElevenLabs
            var textToSpeak = PointingTagParser.StripPointingTags(completeResponseText);
            await _ttsClient.SpeakAsync(textToSpeak, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Interrupted — do not clear history; user may interrupt and ask again
        }
        catch (Exception)
        {
            // Network / API error — surface nothing to the user; just return to idle
        }
        finally
        {
            // TTS completion triggers the state transition via OnTtsPlaybackStopped;
            // if TTS was skipped (empty response or error), transition here.
            if (!_ttsClient.IsPlaying)
                TransitionToVoiceState(VoiceState.Idle);
        }
    }

    private void OnTtsPlaybackStopped(object? sender, EventArgs eventArgs)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _overlayWindow?.ScheduleTransientFadeOut();
        });

        TransitionToVoiceState(VoiceState.Idle);
    }

    // ── Permissions check ─────────────────────────────────────────────────────

    private async Task CheckAndReportPermissions()
    {
        // Microphone: attempt to open and immediately close the default capture device
        bool hasMicrophoneAccess = await CheckMicrophoneAccessAsync();

        // Screen capture: on Windows there is no explicit per-app permission prompt for
        // GDI CopyFromScreen — access is controlled at the OS/group policy level.
        // We assume access is available and catch exceptions during actual capture.
        bool hasScreenCaptureAccess = true;

        PermissionsChanged?.Invoke(this,
            new PermissionsSnapshot(hasMicrophoneAccess, hasScreenCaptureAccess));
    }

    private static async Task<bool> CheckMicrophoneAccessAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var testCapture = new NAudio.Wave.WaveInEvent();
                testCapture.StartRecording();
                testCapture.StopRecording();
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    // ── State transitions ─────────────────────────────────────────────────────

    private void TransitionToVoiceState(VoiceState newVoiceState)
    {
        _currentVoiceState = newVoiceState;
        VoiceStateChanged?.Invoke(this, newVoiceState);
    }

    // ── Event wiring helpers ──────────────────────────────────────────────────

    private void WireAudioCaptureEvents()
    {
        _audioCaptureEngine.AudioChunkAvailable += OnAudioChunkAvailable;
        _audioCaptureEngine.AudioLevelUpdated += OnAudioLevelUpdated;
    }

    private void WireTranscriptionProviderEvents()
    {
        _transcriptionProvider.TranscriptFinalized += OnTranscriptFinalized;
        _transcriptionProvider.TranscriptPartialUpdate += OnTranscriptPartialUpdate;
    }

    private void WirePushToTalkMonitorEvents()
    {
        _pushToTalkMonitor.PushToTalkPressed += OnPushToTalkPressed;
        _pushToTalkMonitor.PushToTalkReleased += OnPushToTalkReleased;
    }

    // ── Overlay window factory ────────────────────────────────────────────────

    private void EnsureOverlayWindowCreated()
    {
        // Must be called on the WPF dispatcher
        if (_overlayWindow == null || !_overlayWindow.IsLoaded)
        {
            _overlayWindow = new OverlayWindow();
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pushToTalkMonitor.PushToTalkPressed -= OnPushToTalkPressed;
        _pushToTalkMonitor.PushToTalkReleased -= OnPushToTalkReleased;
        _pushToTalkMonitor.Stop();
        _pushToTalkMonitor.Dispose();

        _activeTurnCancellationSource?.Cancel();
        _activeTurnCancellationSource?.Dispose();

        _audioCaptureEngine.Dispose();
        _transcriptionProvider.Dispose();
        _claudeApiClient.Dispose();
        _ttsClient.Dispose();

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _overlayWindow?.Close());
    }
}

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
/// Central state machine.  Owns the dictation pipeline, push-to-talk shortcut monitor,
/// screen capture, Claude API client, ElevenLabs TTS, and overlay management.
/// Mirrors CompanionManager.swift in responsibility.
///
/// Chunk 2 adds the real audio engine, AssemblyAI provider, overlay window, and pointing.
/// This shell wires up the event surface so the UI (CompanionPanelWindow) can bind
/// to it immediately.
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

    // Conversation history sent to Claude on every turn
    private readonly List<ConversationMessage> _conversationHistory = new();

    // ── Collaborators (wired up fully in Chunk 2) ─────────────────────────────

    private readonly ClaudeApiClient _claudeApiClient = new();
    private readonly ElevenLabsTtsClient _ttsClient = new();
    private CancellationTokenSource? _activeTurnCancellationSource;

    // ── Startup ───────────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        // Chunk 2: start hotkey monitor, audio engine, AssemblyAI provider
        await Task.CompletedTask;

        // Emit initial permissions state so the panel dots render correctly on first open
        RaisePermissionsChanged();
    }

    // ── Public API called by UI ───────────────────────────────────────────────

    public void SetClaudeModel(string claudeModelId)
    {
        _currentClaudeModel = claudeModelId;
    }

    public void SetCursorOverlayVisible(bool isVisible)
    {
        _isCursorOverlayVisible = isVisible;
        // Chunk 2: show/hide the overlay window
    }

    // ── Push-to-talk entry points (called by GlobalPushToTalkShortcutMonitor) ─

    /// <summary>
    /// Called when the push-to-talk hotkey is pressed.  Transitions to Listening state
    /// and begins microphone capture + streaming transcription.
    /// </summary>
    public void OnPushToTalkPressed()
    {
        if (_currentVoiceState != VoiceState.Idle) return;

        TransitionToVoiceState(VoiceState.Listening);
        // Chunk 2: start audio capture and AssemblyAI session
    }

    /// <summary>
    /// Called when the push-to-talk hotkey is released.  Finalizes the transcript,
    /// captures a screenshot, and kicks off the Claude → TTS pipeline.
    /// </summary>
    public async void OnPushToTalkReleased(string finalizedTranscript)
    {
        if (_currentVoiceState != VoiceState.Listening) return;

        TransitionToVoiceState(VoiceState.Processing);

        _activeTurnCancellationSource?.Cancel();
        _activeTurnCancellationSource = new CancellationTokenSource();
        var cancellationToken = _activeTurnCancellationSource.Token;

        try
        {
            await RunConversationTurnAsync(finalizedTranscript, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // User interrupted — return to idle without clearing history
        }
        catch (Exception)
        {
            // Network/API error — return to idle, do not crash
        }
        finally
        {
            TransitionToVoiceState(VoiceState.Idle);
        }
    }

    // ── Conversation turn pipeline ────────────────────────────────────────────

    private async Task RunConversationTurnAsync(
        string userTranscript,
        CancellationToken cancellationToken)
    {
        // Add user turn to history
        _conversationHistory.Add(new ConversationMessage("user", userTranscript));

        // Chunk 2: capture screenshots here
        var screenCaptures = new List<ScreenCapture>();

        // Stream Claude's response token by token
        var fullResponseText = new System.Text.StringBuilder();

        TransitionToVoiceState(VoiceState.Responding);

        await foreach (var token in _claudeApiClient.StreamResponseAsync(
            _conversationHistory, screenCaptures, _currentClaudeModel, cancellationToken))
        {
            fullResponseText.Append(token);
            // Chunk 2: push token to overlay window for display
        }

        var completeResponseText = fullResponseText.ToString();

        // Add assistant turn to history
        _conversationHistory.Add(new ConversationMessage("assistant", completeResponseText));

        // Chunk 2: parse [POINT:x,y:label:screenN] tags and animate cursor

        // Speak the response via ElevenLabs
        await _ttsClient.SpeakAsync(completeResponseText, cancellationToken);
    }

    // ── State transitions ─────────────────────────────────────────────────────

    private void TransitionToVoiceState(VoiceState newVoiceState)
    {
        _currentVoiceState = newVoiceState;
        VoiceStateChanged?.Invoke(this, newVoiceState);
    }

    private void RaisePermissionsChanged()
    {
        // Chunk 2: check actual microphone and screen capture permissions via Windows APIs
        var permissionsSnapshot = new PermissionsSnapshot(
            HasMicrophoneAccess: false,
            HasScreenCaptureAccess: false);

        PermissionsChanged?.Invoke(this, permissionsSnapshot);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _activeTurnCancellationSource?.Cancel();
        _activeTurnCancellationSource?.Dispose();
        _claudeApiClient.Dispose();
        _ttsClient.Dispose();
    }
}

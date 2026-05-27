using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Clicky;

/// <summary>
/// Streams PCM16 audio to AssemblyAI via WebSocket and delivers finalized turn transcripts.
/// Mirrors AssemblyAIStreamingTranscriptionProvider.swift including:
/// - Token fetch from the Cloudflare Worker (not direct API key usage)
/// - Shared URLSession pattern: a single ClientWebSocket is reused across sessions to avoid
///   OS connection pool corruption (see CLAUDE.md architecture notes)
/// - Turn-based transcript tracking; final text delivered via TranscriptFinalized
/// </summary>
public sealed class AssemblyAiStreamingTranscriptionProvider : IDisposable
{
    // Raised when AssemblyAI delivers a finalized (turn-complete) transcript segment
    public event EventHandler<string>? TranscriptFinalized;

    // Raised for partial (in-progress) transcript updates for live display
    public event EventHandler<string>? TranscriptPartialUpdate;

    // Shared across all sessions — see architecture notes about connection pool corruption
    private static readonly HttpClient SharedHttpClient = new();

    // A single websocket is reused; a new one is created only if the previous was closed
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCancellationSource;
    private bool _isSessionActive;
    private readonly StringBuilder _currentTurnTranscript = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens an AssemblyAI websocket session.  Fetches a short-lived token from the
    /// Cloudflare Worker proxy so the API key never touches the client.
    /// </summary>
    public async Task StartSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_isSessionActive) return;

        var tempToken = await FetchShortLivedTokenAsync(cancellationToken);
        if (tempToken == null) throw new InvalidOperationException("Failed to fetch AssemblyAI session token.");

        var websocketUri = new Uri(
            $"{AppConfig.AssemblyAiWebSocketBase}" +
            $"?token={Uri.EscapeDataString(tempToken)}" +
            $"&sample_rate={AppConfig.AssemblyAiSampleRate}" +
            $"&speech_model={AppConfig.AssemblyAiModel}");

        // Reuse existing socket if still open, otherwise create fresh
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(websocketUri, cancellationToken);
        }

        _currentTurnTranscript.Clear();
        _isSessionActive = true;

        _receiveCancellationSource = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveMessagesLoopAsync(_receiveCancellationSource.Token));
    }

    /// <summary>
    /// Sends a PCM16 audio chunk to AssemblyAI for transcription.
    /// Call this for each chunk from AudioCaptureEngine while the session is active.
    /// </summary>
    public async Task SendAudioChunkAsync(byte[] pcm16AudioData, CancellationToken cancellationToken = default)
    {
        if (!_isSessionActive || _webSocket?.State != WebSocketState.Open) return;

        await _webSocket.SendAsync(
            new ArraySegment<byte>(pcm16AudioData),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Signals to AssemblyAI that the user has released the push-to-talk key.
    /// AssemblyAI will finalize the current turn and emit a session_termination message.
    /// </summary>
    public async Task FinalizeCurrentTurnAsync(CancellationToken cancellationToken = default)
    {
        if (!_isSessionActive || _webSocket?.State != WebSocketState.Open) return;

        // Send the terminate_session message defined in the AssemblyAI v3 WebSocket protocol
        var terminateMessage = JsonSerializer.Serialize(new { terminate_session = true });
        var terminateBytes = Encoding.UTF8.GetBytes(terminateMessage);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(terminateBytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    public void Dispose()
    {
        _receiveCancellationSource?.Cancel();
        _receiveCancellationSource?.Dispose();
        _webSocket?.Dispose();
    }

    // ── Token fetch ───────────────────────────────────────────────────────────

    private static async Task<string?> FetchShortLivedTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SharedHttpClient.PostAsync(
                AppConfig.TranscribeTokenEndpoint,
                content: null,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                return tokenElement.GetString();
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ── WebSocket receive loop ────────────────────────────────────────────────

    private async Task ReceiveMessagesLoopAsync(CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _webSocket?.State == WebSocketState.Open)
            {
                using var messageStream = new System.IO.MemoryStream();
                WebSocketReceiveResult receiveResult;

                // Accumulate fragmented frames into a complete message
                do
                {
                    receiveResult = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer), cancellationToken);
                    messageStream.Write(receiveBuffer, 0, receiveResult.Count);
                }
                while (!receiveResult.EndOfMessage);

                if (receiveResult.MessageType == WebSocketMessageType.Close) break;

                var messageText = Encoding.UTF8.GetString(messageStream.ToArray());
                HandleAssemblyAiMessage(messageText);
            }
        }
        catch (OperationCanceledException) { /* Expected on session end */ }
        catch (WebSocketException) { /* Connection dropped — session will be recreated on next PTT */ }
        finally
        {
            _isSessionActive = false;
        }
    }

    // ── Message parsing ───────────────────────────────────────────────────────

    private void HandleAssemblyAiMessage(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)) return;
            var messageType = typeElement.GetString();

            switch (messageType)
            {
                case "partial_transcript":
                    // Live in-progress text — surface it for display but don't finalize
                    if (root.TryGetProperty("text", out var partialText))
                    {
                        TranscriptPartialUpdate?.Invoke(this, partialText.GetString() ?? string.Empty);
                    }
                    break;

                case "final_transcript":
                    // A turn-complete segment; accumulate into the current turn
                    if (root.TryGetProperty("text", out var finalText))
                    {
                        var text = finalText.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            if (_currentTurnTranscript.Length > 0) _currentTurnTranscript.Append(' ');
                            _currentTurnTranscript.Append(text);
                        }
                    }
                    break;

                case "session_termination":
                    // The session is fully done; deliver the accumulated transcript
                    var completeTranscript = _currentTurnTranscript.ToString().Trim();
                    _currentTurnTranscript.Clear();
                    _isSessionActive = false;

                    if (!string.IsNullOrWhiteSpace(completeTranscript))
                    {
                        TranscriptFinalized?.Invoke(this, completeTranscript);
                    }
                    break;
            }
        }
        catch (JsonException) { /* Malformed message — skip */ }
    }
}

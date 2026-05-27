using System.Net.Http;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace Clicky;

/// <summary>
/// Sends text to the Cloudflare Worker /tts endpoint (which calls ElevenLabs) and plays back
/// the returned MP3 audio through the default audio output device.
/// Exposes IsPlaying so CompanionManager can schedule the cursor fade-out after TTS finishes.
/// </summary>
public sealed class ElevenLabsTtsClient : IDisposable
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private WaveOutEvent? _currentWaveOut;
    private Mp3FileReader? _currentMp3Reader;
    private readonly object _playbackLock = new();

    public bool IsPlaying { get; private set; }

    // Raised on the thread pool when playback finishes (or is stopped)
    public event EventHandler? PlaybackStopped;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches TTS audio for the given text and starts playback immediately.
    /// Stops any currently-playing audio first.
    /// </summary>
    public async Task SpeakAsync(string textToSpeak, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textToSpeak)) return;

        StopPlayback();

        var audioBytes = await FetchTtsAudioBytesAsync(textToSpeak, cancellationToken);
        if (audioBytes == null || audioBytes.Length == 0) return;

        PlayAudioBytes(audioBytes);
    }

    /// <summary>
    /// Immediately stops any in-progress TTS playback.
    /// </summary>
    public void StopPlayback()
    {
        lock (_playbackLock)
        {
            _currentWaveOut?.Stop();
            _currentWaveOut?.Dispose();
            _currentWaveOut = null;

            _currentMp3Reader?.Dispose();
            _currentMp3Reader = null;

            IsPlaying = false;
        }
    }

    public void Dispose() => StopPlayback();

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<byte[]?> FetchTtsAudioBytesAsync(
        string textToSpeak,
        CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            text = textToSpeak,
            model_id = AppConfig.ElevenLabsModel
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AppConfig.TtsEndpoint)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await SharedHttpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Network failure or cancellation — caller handles missing audio gracefully
            return null;
        }
    }

    private void PlayAudioBytes(byte[] mp3Bytes)
    {
        lock (_playbackLock)
        {
            // Mp3FileReader requires a seekable stream; MemoryStream satisfies that
            var memoryStream = new System.IO.MemoryStream(mp3Bytes);
            _currentMp3Reader = new Mp3FileReader(memoryStream);

            _currentWaveOut = new WaveOutEvent();
            _currentWaveOut.Init(_currentMp3Reader);

            _currentWaveOut.PlaybackStopped += (_, _) =>
            {
                lock (_playbackLock) { IsPlaying = false; }
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            };

            IsPlaying = true;
            _currentWaveOut.Play();
        }
    }
}

using NAudio.Wave;

namespace Clicky;

/// <summary>
/// Captures microphone audio via NAudio and converts it to PCM16 mono 16kHz buffers
/// suitable for streaming to AssemblyAI.
/// Mirrors the AVAudioEngine capture layer in BuddyDictationManager.swift.
/// </summary>
public sealed class AudioCaptureEngine : IDisposable
{
    // AssemblyAI requires 16kHz mono PCM16
    private const int TargetSampleRate = 16000;
    private const int TargetChannelCount = 1;
    private const int TargetBitDepth = 16;

    // Raised for each PCM16 audio chunk ready to stream; normalized level in [0,1]
    public event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;

    // Raised each time a new audio level is computed (for the waveform visualizer)
    public event EventHandler<float>? AudioLevelUpdated;

    private WaveInEvent? _waveIn;
    private MediaFoundationResampler? _resampler;
    private bool _isCapturing;

    // ── Public API ────────────────────────────────────────────────────────────

    public void StartCapture()
    {
        if (_isCapturing) return;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(44100, 16, 1), // capture at 44.1kHz then resample
            BufferMilliseconds = 50
        };

        _waveIn.DataAvailable += OnWaveInDataAvailable;
        _waveIn.StartRecording();
        _isCapturing = true;
    }

    public void StopCapture()
    {
        if (!_isCapturing) return;

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
        _isCapturing = false;
    }

    public bool IsCapturing => _isCapturing;

    public void Dispose()
    {
        StopCapture();
        _resampler?.Dispose();
    }

    // ── Audio data handler ────────────────────────────────────────────────────

    private void OnWaveInDataAvailable(object? sender, WaveInEventArgs waveInEventArgs)
    {
        // Resample from 44.1kHz mono PCM16 → 16kHz mono PCM16 for AssemblyAI
        var pcm16Buffer = ResampleTo16kHzMono(
            waveInEventArgs.Buffer,
            waveInEventArgs.BytesRecorded,
            _waveIn!.WaveFormat);

        // Compute RMS audio level for the waveform visualizer (in [0, 1])
        var normalizedAudioLevel = ComputeNormalizedRmsLevel(pcm16Buffer);
        AudioLevelUpdated?.Invoke(this, normalizedAudioLevel);

        AudioChunkAvailable?.Invoke(this, new AudioChunkEventArgs(pcm16Buffer));
    }

    // ── Audio conversion helpers ──────────────────────────────────────────────

    private static byte[] ResampleTo16kHzMono(byte[] sourceBuffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        var targetFormat = new WaveFormat(TargetSampleRate, TargetBitDepth, TargetChannelCount);

        using var sourceStream = new RawSourceWaveStream(
            new System.IO.MemoryStream(sourceBuffer, 0, bytesRecorded), sourceFormat);

        using var resampler = new MediaFoundationResampler(sourceStream, targetFormat)
        {
            ResamplerQuality = 60
        };

        using var outputStream = new System.IO.MemoryStream();
        var readBuffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            outputStream.Write(readBuffer, 0, bytesRead);
        }

        return outputStream.ToArray();
    }

    private static float ComputeNormalizedRmsLevel(byte[] pcm16Buffer)
    {
        if (pcm16Buffer.Length < 2) return 0f;

        double sumOfSquares = 0;
        int sampleCount = pcm16Buffer.Length / 2;

        for (int byteIndex = 0; byteIndex < pcm16Buffer.Length - 1; byteIndex += 2)
        {
            short sample = BitConverter.ToInt16(pcm16Buffer, byteIndex);
            double normalizedSample = sample / 32768.0;
            sumOfSquares += normalizedSample * normalizedSample;
        }

        var rms = Math.Sqrt(sumOfSquares / sampleCount);

        // Apply a mild gain boost so quiet speech is visible in the waveform
        return (float)Math.Min(1.0, rms * 4.0);
    }
}

public sealed class AudioChunkEventArgs(byte[] pcm16Data) : EventArgs
{
    public byte[] Pcm16Data { get; } = pcm16Data;
}

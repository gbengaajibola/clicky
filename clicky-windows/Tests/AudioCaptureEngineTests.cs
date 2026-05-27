using Xunit;

namespace Clicky.Tests;

/// <summary>
/// Tests for AudioCaptureEngine's pure data-processing helpers.
/// The NAudio device access is not exercised here (requires a physical audio device);
/// only the PCM math and event surface are tested.
/// </summary>
public sealed class AudioCaptureEngineTests
{
    // AudioCaptureEngine exposes IsCapturing — verify the default state
    [Fact]
    public void AudioCaptureEngine_InitialState_IsNotCapturing()
    {
        using var engine = new AudioCaptureEngine();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void AudioCaptureEngine_StopCalledWithoutStart_DoesNotThrow()
    {
        using var engine = new AudioCaptureEngine();
        var exception = Record.Exception(() => engine.StopCapture());
        Assert.Null(exception);
    }

    [Fact]
    public void AudioCaptureEngine_DisposeCalledTwice_DoesNotThrow()
    {
        var engine = new AudioCaptureEngine();
        engine.Dispose();
        var exception = Record.Exception(() => engine.Dispose());
        Assert.Null(exception);
    }

    // AudioChunkEventArgs stores PCM data correctly
    [Fact]
    public void AudioChunkEventArgs_StoresPcm16Data()
    {
        var pcm16Data = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var eventArgs = new AudioChunkEventArgs(pcm16Data);

        Assert.Equal(pcm16Data, eventArgs.Pcm16Data);
    }
}

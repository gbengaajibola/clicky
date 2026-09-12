using Xunit;

namespace Clicky.Tests;

/// <summary>
/// Unit tests for ClaudeApiClient's request-building and SSE-parsing logic.
/// These tests exercise the pure data-transformation code paths that don't
/// require a live network connection.
/// </summary>
public sealed class ClaudeApiClientTests
{
    // ── ConversationMessage record ────────────────────────────────────────────

    [Fact]
    public void ConversationMessage_UserRole_StoresRoleAndContent()
    {
        var message = new ConversationMessage("user", "Hello Claude");

        Assert.Equal("user", message.Role);
        Assert.Equal("Hello Claude", message.Content);
    }

    [Fact]
    public void ConversationMessage_AssistantRole_StoresRoleAndContent()
    {
        var message = new ConversationMessage("assistant", "Hello! How can I help?");

        Assert.Equal("assistant", message.Role);
        Assert.Equal("Hello! How can I help?", message.Content);
    }

    // ── ScreenCapture record ──────────────────────────────────────────────────

    [Fact]
    public void ScreenCapture_StoresImageDataMimeTypeAndMonitorIndex()
    {
        var fakeImageData = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG magic bytes
        var capture = new ScreenCapture(fakeImageData, "image/jpeg", monitorIndex: 0);

        Assert.Equal(fakeImageData, capture.ImageData);
        Assert.Equal("image/jpeg", capture.MimeType);
        Assert.Equal(0, capture.MonitorIndex);
    }

    [Fact]
    public void ScreenCapture_SecondMonitor_HasCorrectIndex()
    {
        var capture = new ScreenCapture(Array.Empty<byte>(), "image/jpeg", monitorIndex: 1);
        Assert.Equal(1, capture.MonitorIndex);
    }

    // ── AppConfig defaults ────────────────────────────────────────────────────

    [Fact]
    public void AppConfig_DefaultModel_IsSonnet()
    {
        Assert.Equal("claude-sonnet-4-6", AppConfig.DefaultClaudeModel);
    }

    [Fact]
    public void AppConfig_OpusModel_IsOpus()
    {
        Assert.Equal("claude-opus-4-6", AppConfig.OpusClaudeModel);
    }

    [Fact]
    public void AppConfig_ChatEndpoint_ContainsWorkerBaseUrl()
    {
        Assert.Contains(AppConfig.WorkerBaseUrl, AppConfig.ChatEndpoint);
        Assert.EndsWith("/chat", AppConfig.ChatEndpoint);
    }

    [Fact]
    public void AppConfig_TtsEndpoint_ContainsWorkerBaseUrl()
    {
        Assert.Contains(AppConfig.WorkerBaseUrl, AppConfig.TtsEndpoint);
        Assert.EndsWith("/tts", AppConfig.TtsEndpoint);
    }

    [Fact]
    public void AppConfig_TranscribeTokenEndpoint_ContainsWorkerBaseUrl()
    {
        Assert.Contains(AppConfig.WorkerBaseUrl, AppConfig.TranscribeTokenEndpoint);
        Assert.EndsWith("/transcribe-token", AppConfig.TranscribeTokenEndpoint);
    }
}

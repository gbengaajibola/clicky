namespace Clicky;

/// <summary>
/// Runtime configuration. All sensitive values (API keys) live on the Cloudflare Worker;
/// this class only holds the worker base URL and client-side preferences.
/// </summary>
public static class AppConfig
{
    // Base URL for the Cloudflare Worker proxy.  Override via CLICKY_WORKER_URL env var
    // at build time so different environments (dev/staging/prod) can point to different workers.
    public static string WorkerBaseUrl { get; } =
        Environment.GetEnvironmentVariable("CLICKY_WORKER_URL")
        ?? "https://clicky-worker.YOUR_SUBDOMAIN.workers.dev";

    // Full endpoint URLs derived from the base
    public static string ChatEndpoint => $"{WorkerBaseUrl}/chat";
    public static string TtsEndpoint => $"{WorkerBaseUrl}/tts";
    public static string TranscribeTokenEndpoint => $"{WorkerBaseUrl}/transcribe-token";

    // Default Claude model sent in the /chat request body.  The UI lets the user switch
    // between Sonnet (fast) and Opus (powerful) at runtime.
    public const string DefaultClaudeModel = "claude-sonnet-4-6";
    public const string OpusClaudeModel = "claude-opus-4-6";

    // AssemblyAI websocket base — the worker hands back a short-lived token and we connect here
    public const string AssemblyAiWebSocketBase = "wss://streaming.assemblyai.com/v3/ws";
    public const string AssemblyAiSampleRate = "16000";
    public const string AssemblyAiModel = "u3-rt-pro";

    // ElevenLabs model used by the worker-side TTS request
    public const string ElevenLabsModel = "eleven_flash_v2_5";

    // Push-to-talk virtual key combination: Ctrl + Alt (≈ macOS ctrl+option)
    public const int PttModifierVirtualKey = 0xA2; // VK_LCONTROL
    public const int PttTriggerVirtualKey = 0xA4;  // VK_LMENU (left Alt)

    // PostHog analytics project API key (non-sensitive — public write-only key)
    public static string PostHogApiKey { get; } =
        Environment.GetEnvironmentVariable("CLICKY_POSTHOG_KEY") ?? string.Empty;
    public const string PostHogHost = "https://app.posthog.com";
}

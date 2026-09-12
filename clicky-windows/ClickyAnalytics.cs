using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Clicky;

/// <summary>
/// PostHog analytics integration.  Mirrors ClickyAnalytics.swift.
/// All events are fire-and-forget; analytics failures must never affect the main pipeline.
/// The PostHog API key is a public write-only key — safe to ship in the app.
/// </summary>
public static class ClickyAnalytics
{
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly string AnonymousDistinctId = GetOrCreateAnonymousId();

    // ── Public event surface ──────────────────────────────────────────────────

    public static void TrackPushToTalkStarted(string claudeModel)
    {
        if (string.IsNullOrEmpty(AppConfig.PostHogApiKey)) return;
        SendEvent("ptt_started", new Dictionary<string, object>
        {
            ["model"] = claudeModel,
            ["platform"] = "windows"
        });
    }

    public static void TrackResponseReceived(int responseCharacterCount, string claudeModel)
    {
        if (string.IsNullOrEmpty(AppConfig.PostHogApiKey)) return;
        SendEvent("response_received", new Dictionary<string, object>
        {
            ["char_count"] = responseCharacterCount,
            ["model"] = claudeModel,
            ["platform"] = "windows"
        });
    }

    public static void TrackPointingAnimationTriggered(int pointingTargetCount)
    {
        if (string.IsNullOrEmpty(AppConfig.PostHogApiKey)) return;
        SendEvent("pointing_animation_triggered", new Dictionary<string, object>
        {
            ["target_count"] = pointingTargetCount,
            ["platform"] = "windows"
        });
    }

    public static void TrackTranscriptionError(string errorDescription)
    {
        if (string.IsNullOrEmpty(AppConfig.PostHogApiKey)) return;
        SendEvent("transcription_error", new Dictionary<string, object>
        {
            ["error"] = errorDescription,
            ["platform"] = "windows"
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void SendEvent(string eventName, Dictionary<string, object> properties)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    api_key = AppConfig.PostHogApiKey,
                    @event = eventName,
                    distinct_id = AnonymousDistinctId,
                    properties
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                await SharedHttpClient.PostAsync($"{AppConfig.PostHogHost}/capture/", content);
            }
            catch
            {
                // Analytics failures are always swallowed — never affect the main pipeline
            }
        });
    }

    private static string GetOrCreateAnonymousId()
    {
        // Persist a random anonymous ID in LocalApplicationData so it survives sessions
        // without identifying the user
        const string idFileName = "clicky_anonymous_id.txt";
        var appDataPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clicky");

        System.IO.Directory.CreateDirectory(appDataPath);
        var idFilePath = System.IO.Path.Combine(appDataPath, idFileName);

        if (System.IO.File.Exists(idFilePath))
        {
            var existingId = System.IO.File.ReadAllText(idFilePath).Trim();
            if (!string.IsNullOrEmpty(existingId)) return existingId;
        }

        var newId = Guid.NewGuid().ToString();
        System.IO.File.WriteAllText(idFilePath, newId);
        return newId;
    }
}

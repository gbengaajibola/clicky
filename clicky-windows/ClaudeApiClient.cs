using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clicky;

/// <summary>
/// Sends conversation turns (text + optional screenshots) to the Cloudflare Worker /chat
/// endpoint, which proxies to Claude.  Supports SSE streaming so partial tokens are
/// delivered to the UI as they arrive.
/// </summary>
public sealed class ClaudeApiClient : IDisposable
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Streams response tokens from Claude given conversation history plus an optional
    /// set of screen captures (one per monitor).  Each yielded string is a partial token.
    /// The async enumerable completes when the SSE stream closes or the caller cancels.
    /// </summary>
    public async IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<ScreenCapture> screenCaptures,
        string claudeModel,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(conversationHistory, screenCaptures, claudeModel);
        var requestJson = JsonSerializer.Serialize(requestBody);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AppConfig.ChatEndpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("Accept", "text/event-stream");

        using var response = await SharedHttpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(responseStream);

        // SSE format: lines starting with "data: " carry JSON payloads;
        // "data: [DONE]" signals stream end.
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var payload = line["data: ".Length..];
            if (payload == "[DONE]") break;

            var token = ExtractTextTokenFromSsePayload(payload);
            if (token != null) yield return token;
        }
    }

    /// <summary>
    /// Non-streaming variant.  Sends the full request and returns the complete response text
    /// once Claude finishes.  Used for short prompts where latency is not critical.
    /// </summary>
    public async Task<string> GetCompleteResponseAsync(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<ScreenCapture> screenCaptures,
        string claudeModel,
        CancellationToken cancellationToken = default)
    {
        var tokens = new StringBuilder();
        await foreach (var token in StreamResponseAsync(
            conversationHistory, screenCaptures, claudeModel, cancellationToken))
        {
            tokens.Append(token);
        }
        return tokens.ToString();
    }

    public void Dispose() { /* SharedHttpClient is static — never disposed */ }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static object BuildRequestBody(
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<ScreenCapture> screenCaptures,
        string claudeModel)
    {
        // Convert conversation history into Anthropic message format
        var messages = new List<object>();

        foreach (var historyMessage in conversationHistory)
        {
            messages.Add(new
            {
                role = historyMessage.Role,
                content = historyMessage.Content
            });
        }

        // Attach screen captures as image blocks in the final user message if present
        if (screenCaptures.Count > 0)
        {
            var imageBlocks = new List<object>();

            foreach (var capture in screenCaptures)
            {
                imageBlocks.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = capture.MimeType,
                        data = Convert.ToBase64String(capture.ImageData)
                    }
                });
                // Label each monitor capture so Claude can reference "Screen 1", etc.
                imageBlocks.Add(new
                {
                    type = "text",
                    text = $"[Screen {capture.MonitorIndex + 1}]"
                });
            }

            // Merge image blocks into the last user message, or create a new one
            if (messages.Count > 0 &&
                messages[^1] is { } lastMessage &&
                lastMessage.GetType().GetProperty("role")?.GetValue(lastMessage) as string == "user")
            {
                // Replace the last user message with an enriched multipart version
                var lastUserText = (messages[^1].GetType()
                    .GetProperty("content")?.GetValue(messages[^1]) as string) ?? string.Empty;

                var contentBlocks = new List<object>(imageBlocks)
                {
                    new { type = "text", text = lastUserText }
                };
                messages[^1] = new { role = "user", content = contentBlocks };
            }
            else
            {
                messages.Add(new { role = "user", content = imageBlocks });
            }
        }

        return new
        {
            model = claudeModel,
            max_tokens = 4096,
            stream = true,
            messages
        };
    }

    private static string? ExtractTextTokenFromSsePayload(string ssePayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(ssePayload);
            var root = doc.RootElement;

            // Anthropic SSE format: { "type": "content_block_delta", "delta": { "type": "text_delta", "text": "..." } }
            if (!root.TryGetProperty("type", out var typeElement)) return null;
            if (typeElement.GetString() != "content_block_delta") return null;

            if (!root.TryGetProperty("delta", out var delta)) return null;
            if (!delta.TryGetProperty("text", out var textElement)) return null;

            return textElement.GetString();
        }
        catch (JsonException)
        {
            // Malformed SSE payload — skip silently
            return null;
        }
    }
}

// ── Supporting data types ─────────────────────────────────────────────────────

public record ConversationMessage(string Role, object Content);

public record ScreenCapture(byte[] ImageData, string MimeType, int MonitorIndex);

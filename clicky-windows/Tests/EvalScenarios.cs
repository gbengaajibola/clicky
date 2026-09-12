using System.Text.RegularExpressions;
using Xunit;

namespace Clicky.Tests;

/// <summary>
/// End-to-end evaluation scenarios that validate the full response pipeline
/// without hitting live APIs.  These use representative Claude response fixtures
/// to verify that the tag parser, text cleanup, and pointing coordinate extraction
/// all behave correctly on realistic inputs.
/// </summary>
public sealed class EvalScenarios
{
    // ── Fixture: typical Claude response with pointing tags ───────────────────

    private const string TypicalClaudeResponseWithPointingTags = """
        Sure! To save your document in VS Code, click the **File** menu
        [POINT:0.05,0.02:File menu:screen1] at the top left, then select
        **Save** [POINT:0.05,0.08:Save option:screen1]. You can also use
        Ctrl+S as a keyboard shortcut.
        """;

    // ── Eval: tag extraction on realistic response ────────────────────────────

    [Fact]
    public void Eval_TypicalResponse_ExtractsTwoPointingTargets()
    {
        var targets = PointingTagParser.ExtractPointingTargets(TypicalClaudeResponseWithPointingTags);

        Assert.Equal(2, targets.Count);
        Assert.Equal("File menu", targets[0].Label);
        Assert.Equal("Save option", targets[1].Label);
    }

    [Fact]
    public void Eval_TypicalResponse_CoordinatesAreInNormalizedRange()
    {
        var targets = PointingTagParser.ExtractPointingTargets(TypicalClaudeResponseWithPointingTags);

        foreach (var target in targets)
        {
            Assert.InRange(target.NormalizedX, 0.0, 1.0);
            Assert.InRange(target.NormalizedY, 0.0, 1.0);
        }
    }

    [Fact]
    public void Eval_TypicalResponse_StrippedTextContainsNoTagSyntax()
    {
        var stripped = PointingTagParser.StripPointingTags(TypicalClaudeResponseWithPointingTags);

        Assert.DoesNotContain("[POINT:", stripped);
        Assert.DoesNotContain(":screen1]", stripped);
    }

    [Fact]
    public void Eval_TypicalResponse_StrippedTextPreservesUserFacingContent()
    {
        var stripped = PointingTagParser.StripPointingTags(TypicalClaudeResponseWithPointingTags);

        Assert.Contains("File", stripped);
        Assert.Contains("Save", stripped);
        Assert.Contains("Ctrl+S", stripped);
    }

    // ── Eval: response with no pointing tags ─────────────────────────────────

    private const string ClaudeResponseWithoutTags = """
        The capital of France is Paris. It is located in the north-central
        part of the country along the Seine River.
        """;

    [Fact]
    public void Eval_ResponseWithoutTags_ExtractsNoTargets()
    {
        var targets = PointingTagParser.ExtractPointingTargets(ClaudeResponseWithoutTags);
        Assert.Empty(targets);
    }

    [Fact]
    public void Eval_ResponseWithoutTags_StripIsIdentity()
    {
        var stripped = PointingTagParser.StripPointingTags(ClaudeResponseWithoutTags);
        // Stripped text should equal the original after trimming
        Assert.Equal(ClaudeResponseWithoutTags.Trim(), stripped.Trim());
    }

    // ── Eval: multi-monitor response ──────────────────────────────────────────

    private const string MultiMonitorClaudeResponse = """
        Your browser is on screen 1 [POINT:0.5,0.5:Browser window:screen1] and
        your code editor is on screen 2 [POINT:0.3,0.4:Editor window:screen2].
        """;

    [Fact]
    public void Eval_MultiMonitorResponse_AssignsCorrectMonitorIndices()
    {
        var targets = PointingTagParser.ExtractPointingTargets(MultiMonitorClaudeResponse);

        Assert.Equal(2, targets.Count);
        Assert.Equal(0, targets[0].ZeroBasedMonitorIndex); // screen1 → 0
        Assert.Equal(1, targets[1].ZeroBasedMonitorIndex); // screen2 → 1
    }

    // ── Eval: AppConfig endpoint shape ───────────────────────────────────────

    [Fact]
    public void Eval_AllEndpoints_AreWellFormedUrls()
    {
        Assert.True(Uri.TryCreate(AppConfig.ChatEndpoint, UriKind.Absolute, out _),
            $"ChatEndpoint is not a valid URL: {AppConfig.ChatEndpoint}");

        Assert.True(Uri.TryCreate(AppConfig.TtsEndpoint, UriKind.Absolute, out _),
            $"TtsEndpoint is not a valid URL: {AppConfig.TtsEndpoint}");

        Assert.True(Uri.TryCreate(AppConfig.TranscribeTokenEndpoint, UriKind.Absolute, out _),
            $"TranscribeTokenEndpoint is not a valid URL: {AppConfig.TranscribeTokenEndpoint}");
    }

    // ── Eval: AssemblyAI websocket URI construction ───────────────────────────

    [Fact]
    public void Eval_AssemblyAiWebSocketBase_IsWebSocketUri()
    {
        Assert.StartsWith("wss://", AppConfig.AssemblyAiWebSocketBase);
    }

    [Fact]
    public void Eval_AssemblyAiModel_IsExpectedModel()
    {
        Assert.Equal("u3-rt-pro", AppConfig.AssemblyAiModel);
    }
}

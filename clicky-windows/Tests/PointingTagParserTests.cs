using Xunit;

namespace Clicky.Tests;

/// <summary>
/// Unit tests for PointingTagParser — the component that extracts [POINT:...] tags
/// from Claude's response text and strips them for clean display / TTS.
/// These tests are self-contained and have no external dependencies.
/// </summary>
public sealed class PointingTagParserTests
{
    // ── ExtractPointingTargets ────────────────────────────────────────────────

    [Fact]
    public void ExtractPointingTargets_SingleTag_ReturnsSingleTarget()
    {
        var claudeResponse = "Click the button [POINT:0.5,0.75:Submit button:screen1] to continue.";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Single(targets);
        Assert.Equal(0.5, targets[0].NormalizedX, precision: 4);
        Assert.Equal(0.75, targets[0].NormalizedY, precision: 4);
        Assert.Equal("Submit button", targets[0].Label);
        Assert.Equal(0, targets[0].ZeroBasedMonitorIndex); // screen1 → index 0
    }

    [Fact]
    public void ExtractPointingTargets_MultipleTags_ReturnsAllTargetsInOrder()
    {
        var claudeResponse =
            "First [POINT:0.1,0.2:File menu:screen1] then [POINT:0.8,0.9:Save icon:screen2].";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Equal(2, targets.Count);

        Assert.Equal(0.1, targets[0].NormalizedX, precision: 4);
        Assert.Equal("File menu", targets[0].Label);
        Assert.Equal(0, targets[0].ZeroBasedMonitorIndex);

        Assert.Equal(0.8, targets[1].NormalizedX, precision: 4);
        Assert.Equal("Save icon", targets[1].Label);
        Assert.Equal(1, targets[1].ZeroBasedMonitorIndex); // screen2 → index 1
    }

    [Fact]
    public void ExtractPointingTargets_NoTags_ReturnsEmptyList()
    {
        var claudeResponse = "There are no pointing tags in this response.";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Empty(targets);
    }

    [Fact]
    public void ExtractPointingTargets_EmptyString_ReturnsEmptyList()
    {
        var targets = PointingTagParser.ExtractPointingTargets(string.Empty);
        Assert.Empty(targets);
    }

    [Fact]
    public void ExtractPointingTargets_MalformedTag_IsSkipped()
    {
        // Missing the screen part — should not parse
        var claudeResponse = "Try [POINT:0.5,0.5:label] clicking here.";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Empty(targets);
    }

    [Fact]
    public void ExtractPointingTargets_CaseInsensitive_ParsesSuccessfully()
    {
        var claudeResponse = "See [point:0.3,0.4:Close button:screen1] here.";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Single(targets);
        Assert.Equal("Close button", targets[0].Label);
    }

    [Fact]
    public void ExtractPointingTargets_CoordinatesNormalizedToZeroOne_ArePreservedExactly()
    {
        var claudeResponse = "[POINT:0.0,1.0:Corner:screen1]";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Single(targets);
        Assert.Equal(0.0, targets[0].NormalizedX, precision: 6);
        Assert.Equal(1.0, targets[0].NormalizedY, precision: 6);
    }

    [Fact]
    public void ExtractPointingTargets_LabelWithSpaces_PreservesFullLabel()
    {
        var claudeResponse = "[POINT:0.5,0.5:The quick brown fox:screen1]";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);

        Assert.Equal("The quick brown fox", targets[0].Label);
    }

    // ── StripPointingTags ─────────────────────────────────────────────────────

    [Fact]
    public void StripPointingTags_ResponseWithTags_RemovesAllTags()
    {
        var claudeResponse =
            "Click [POINT:0.5,0.5:Submit:screen1] the button and then [POINT:0.1,0.9:Quit:screen1] to exit.";

        var stripped = PointingTagParser.StripPointingTags(claudeResponse);

        Assert.DoesNotContain("[POINT:", stripped);
        Assert.Contains("Click", stripped);
        Assert.Contains("the button", stripped);
        Assert.Contains("to exit.", stripped);
    }

    [Fact]
    public void StripPointingTags_NoTags_ReturnsOriginalText()
    {
        var originalText = "No tags here, just plain text.";

        var stripped = PointingTagParser.StripPointingTags(originalText);

        Assert.Equal(originalText, stripped);
    }

    [Fact]
    public void StripPointingTags_EmptyString_ReturnsEmptyString()
    {
        var stripped = PointingTagParser.StripPointingTags(string.Empty);
        Assert.Equal(string.Empty, stripped);
    }

    [Fact]
    public void StripPointingTags_OnlyTag_ReturnsEmptyOrWhitespace()
    {
        var stripped = PointingTagParser.StripPointingTags("[POINT:0.5,0.5:label:screen1]");
        Assert.True(string.IsNullOrWhiteSpace(stripped));
    }

    // ── Round-trip: extract then strip ───────────────────────────────────────

    [Fact]
    public void RoundTrip_ExtractTargetsThenStripTags_TagsRemovedAndDataPreserved()
    {
        var claudeResponse =
            "Look at [POINT:0.25,0.33:Settings gear:screen1] and [POINT:0.75,0.66:Profile icon:screen2].";

        var targets = PointingTagParser.ExtractPointingTargets(claudeResponse);
        var cleanText = PointingTagParser.StripPointingTags(claudeResponse);

        Assert.Equal(2, targets.Count);
        Assert.DoesNotContain("[POINT:", cleanText);
        Assert.Contains("Look at", cleanText);
        Assert.Contains("and", cleanText);
    }
}

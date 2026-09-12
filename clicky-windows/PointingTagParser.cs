using System.Text.RegularExpressions;

namespace Clicky;

/// <summary>
/// Parses [POINT:x,y:label:screenN] tags embedded in Claude's response text.
/// Mirrors ElementLocationDetector.swift / the tag parsing in OverlayWindow.swift.
/// </summary>
public static class PointingTagParser
{
    // Matches [POINT:x,y:label:screenN] where x,y are floats in [0,1] (normalized),
    // label is arbitrary text, and N is the 1-based monitor index.
    // Example: [POINT:0.42,0.71:Submit button:screen1]
    private static readonly Regex PointTagRegex = new(
        @"\[POINT:(?<x>[0-9]*\.?[0-9]+),(?<y>[0-9]*\.?[0-9]+):(?<label>[^:]+):screen(?<screen>\d+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extracts all pointing targets from a Claude response string.
    /// Returns them in the order they appear in the text.
    /// </summary>
    public static IReadOnlyList<PointingTarget> ExtractPointingTargets(string claudeResponseText)
    {
        var targets = new List<PointingTarget>();

        foreach (Match match in PointTagRegex.Matches(claudeResponseText))
        {
            if (!double.TryParse(match.Groups["x"].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var normalizedX)) continue;

            if (!double.TryParse(match.Groups["y"].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var normalizedY)) continue;

            if (!int.TryParse(match.Groups["screen"].Value, out var oneBasedScreenIndex)) continue;

            var label = match.Groups["label"].Value.Trim();
            var zeroBasedMonitorIndex = oneBasedScreenIndex - 1;

            targets.Add(new PointingTarget(normalizedX, normalizedY, label, zeroBasedMonitorIndex));
        }

        return targets;
    }

    /// <summary>
    /// Removes all [POINT:...] tags from a Claude response so the clean text can be
    /// displayed to the user and read aloud by TTS without the raw tag syntax.
    /// </summary>
    public static string StripPointingTags(string claudeResponseText)
    {
        return PointTagRegex.Replace(claudeResponseText, string.Empty).Trim();
    }
}

/// <summary>
/// A single UI element pointing target extracted from a Claude response.
/// Coordinates are normalized (0–1 relative to the monitor bounds).
/// </summary>
public record PointingTarget(
    double NormalizedX,
    double NormalizedY,
    string Label,
    int ZeroBasedMonitorIndex);

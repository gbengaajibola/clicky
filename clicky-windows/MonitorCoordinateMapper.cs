using System.Windows;
using System.Windows.Forms;

namespace Clicky;

/// <summary>
/// Maps between monitor indices (as referenced in Claude's [POINT:...] tags) and
/// the absolute screen pixel bounds of each connected display.
/// Mirrors the multi-monitor coordinate mapping in OverlayWindow.swift.
/// </summary>
public static class MonitorCoordinateMapper
{
    /// <summary>
    /// Returns the absolute pixel bounds of the monitor at the given zero-based index,
    /// or null if the index is out of range.
    /// </summary>
    public static Rect? GetMonitorBounds(int zeroBasedMonitorIndex)
    {
        var allScreens = Screen.AllScreens;
        if (zeroBasedMonitorIndex < 0 || zeroBasedMonitorIndex >= allScreens.Length)
            return null;

        var screen = allScreens[zeroBasedMonitorIndex];
        return new Rect(
            screen.Bounds.X,
            screen.Bounds.Y,
            screen.Bounds.Width,
            screen.Bounds.Height);
    }

    /// <summary>
    /// Converts a normalized [0,1] coordinate relative to a specific monitor into
    /// absolute screen pixels on that monitor.
    /// </summary>
    public static Point? NormalizedToAbsoluteScreenPixels(
        double normalizedX,
        double normalizedY,
        int zeroBasedMonitorIndex)
    {
        var bounds = GetMonitorBounds(zeroBasedMonitorIndex);
        if (bounds == null) return null;

        return new Point(
            bounds.Value.X + normalizedX * bounds.Value.Width,
            bounds.Value.Y + normalizedY * bounds.Value.Height);
    }

    /// <summary>
    /// Returns how many monitors are currently connected.
    /// </summary>
    public static int ConnectedMonitorCount => Screen.AllScreens.Length;
}

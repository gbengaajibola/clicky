using System.Windows;
using System.Windows.Forms;

namespace Clicky;

/// <summary>
/// Helpers for placing windows relative to monitor geometry and querying Windows
/// accessibility/microphone permission state.
/// Mirrors WindowPositionManager.swift.
/// </summary>
public static class WindowPositionManager
{
    /// <summary>
    /// Returns the work area of the monitor closest to the given screen point.
    /// Used by SystemTrayManager to position the panel near the tray icon.
    /// </summary>
    public static Rect GetWorkAreaNearPoint(Point screenPoint)
    {
        var nearestScreen = Screen.FromPoint(
            new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));

        var workArea = nearestScreen.WorkingArea;
        return new Rect(workArea.X, workArea.Y, workArea.Width, workArea.Height);
    }

    /// <summary>
    /// Clamps a window's desired position so it stays fully within the nearest monitor's
    /// work area.  Returns an adjusted top-left position.
    /// </summary>
    public static Point ClampWindowToWorkArea(
        Point desiredTopLeft,
        double windowWidth,
        double windowHeight)
    {
        var workArea = GetWorkAreaNearPoint(desiredTopLeft);

        var clampedLeft = Math.Max(workArea.Left,
            Math.Min(desiredTopLeft.X, workArea.Right - windowWidth));

        var clampedTop = Math.Max(workArea.Top,
            Math.Min(desiredTopLeft.Y, workArea.Bottom - windowHeight));

        return new Point(clampedLeft, clampedTop);
    }

    /// <summary>
    /// Returns true if the app has been granted microphone access by the user.
    /// On Windows 10/11 this is controlled via Settings → Privacy → Microphone.
    /// </summary>
    public static bool HasMicrophoneAccess()
    {
        try
        {
            // The most reliable check without COM/WinRT: attempt to open the default
            // capture device and immediately release it.
            using var waveIn = new NAudio.Wave.WaveInEvent();
            waveIn.StartRecording();
            waveIn.StopRecording();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

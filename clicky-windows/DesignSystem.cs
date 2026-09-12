using System.Windows;
using System.Windows.Media;

namespace Clicky;

/// <summary>
/// Central design token registry. All UI code references DS.Colors, DS.CornerRadius, etc.
/// Mirrors DesignSystem.swift so the two codebases stay conceptually aligned.
/// </summary>
public static class DS
{
    public static class Colors
    {
        // Panel background — matches macOS companion dark aesthetic
        public static readonly Color PanelBackground = Color.FromRgb(18, 18, 20);
        public static readonly SolidColorBrush PanelBackgroundBrush = new(PanelBackground);

        // Surface used for cards / list rows inside the panel
        public static readonly Color Surface = Color.FromRgb(28, 28, 32);
        public static readonly SolidColorBrush SurfaceBrush = new(Surface);

        // Primary accent — blue used for the cursor and active states
        public static readonly Color Accent = Color.FromRgb(59, 130, 246);
        public static readonly SolidColorBrush AccentBrush = new(Accent);

        // Text hierarchy
        public static readonly Color TextPrimary = Color.FromRgb(242, 242, 247);
        public static readonly SolidColorBrush TextPrimaryBrush = new(TextPrimary);

        public static readonly Color TextSecondary = Color.FromRgb(142, 142, 147);
        public static readonly SolidColorBrush TextSecondaryBrush = new(TextSecondary);

        public static readonly Color TextTertiary = Color.FromRgb(99, 99, 102);
        public static readonly SolidColorBrush TextTertiaryBrush = new(TextTertiary);

        // Separator / border
        public static readonly Color Border = Color.FromArgb(40, 255, 255, 255);
        public static readonly SolidColorBrush BorderBrush = new(Border);

        // Recording-active indicator
        public static readonly Color RecordingRed = Color.FromRgb(255, 59, 48);
        public static readonly SolidColorBrush RecordingRedBrush = new(RecordingRed);

        // Waveform bars when idle
        public static readonly Color WaveformIdle = Color.FromArgb(100, 142, 142, 147);
        public static readonly SolidColorBrush WaveformIdleBrush = new(WaveformIdle);

        // Waveform bars when actively recording
        public static readonly Color WaveformActive = Color.FromArgb(200, 59, 130, 246);
        public static readonly SolidColorBrush WaveformActiveBrush = new(WaveformActive);

        // Cursor companion body color
        public static readonly Color CursorBlue = Color.FromRgb(59, 130, 246);
        public static readonly SolidColorBrush CursorBlueBrush = new(CursorBlue);
    }

    public static class CornerRadius
    {
        public static readonly CornerRadius Panel = new(16);
        public static readonly CornerRadius Card = new(10);
        public static readonly CornerRadius Button = new(8);
        public static readonly CornerRadius Pill = new(100);
    }

    public static class Spacing
    {
        public const double XSmall = 4;
        public const double Small = 8;
        public const double Medium = 12;
        public const double Large = 16;
        public const double XLarge = 24;
    }

    public static class FontSize
    {
        public const double Caption = 11;
        public const double Body = 13;
        public const double Callout = 14;
        public const double Headline = 17;
        public const double Title = 22;
    }

    public static class Animation
    {
        // Duration for panel show/hide fade
        public static readonly TimeSpan PanelFadeDuration = TimeSpan.FromMilliseconds(150);

        // Duration for cursor bezier arc travel
        public static readonly TimeSpan CursorTravelDuration = TimeSpan.FromMilliseconds(600);

        // Duration for cursor fade in/out in transient mode
        public static readonly TimeSpan CursorFadeDuration = TimeSpan.FromMilliseconds(250);
    }
}

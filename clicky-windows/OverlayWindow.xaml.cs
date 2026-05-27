using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Clicky;

/// <summary>
/// Full-screen transparent overlay window that hosts the blue cursor, response text bubble,
/// and spinner.  Mirrors OverlayWindow.swift:
/// - Non-activating (IsHitTestVisible=false) — never steals focus
/// - Spans the primary monitor; multi-monitor pointing maps coordinates via MonitorCoordinateMapper
/// - Cursor animates along a bezier arc to pointing targets
/// - Fades out automatically after a period of inactivity in transient (cursor-hidden) mode
/// </summary>
public partial class OverlayWindow : Window
{
    // How long after the last interaction before the overlay auto-fades in transient mode
    private static readonly TimeSpan TransientFadeOutDelay = TimeSpan.FromSeconds(1);

    private System.Windows.Threading.DispatcherTimer? _transientFadeOutTimer;
    private bool _isTransientMode; // true when "Show Clicky" is off

    // Current cursor position on screen (absolute pixels)
    private Point _cursorScreenPosition;

    public OverlayWindow()
    {
        InitializeComponent();
        StretchToFullPrimaryScreen();
    }

    // ── Public API called by CompanionManager ─────────────────────────────────

    public void ShowOverlay(bool isTransientMode)
    {
        _isTransientMode = isTransientMode;
        CancelTransientFadeOut();

        if (isTransientMode)
        {
            Opacity = 0;
            Show();
            FadeOverlayOpacityTo(targetOpacity: 1.0, duration: DS.Animation.CursorFadeDuration);
        }
        else
        {
            Opacity = 1;
            Show();
        }
    }

    public void HideOverlay()
    {
        if (_isTransientMode)
        {
            FadeOverlayOpacityTo(targetOpacity: 0, duration: DS.Animation.CursorFadeDuration,
                onCompleted: () => Hide());
        }
        else
        {
            Hide();
        }
    }

    public void ShowCursorAtCurrentMousePosition()
    {
        var mousePosition = GetCurrentMouseScreenPosition();
        PlaceCursorAt(mousePosition);
        CursorCanvas.Visibility = Visibility.Visible;
    }

    public void ShowSpinner()
    {
        var center = GetOverlayCenterPoint();
        Canvas.SetLeft(SpinnerCanvas, center.X - 16);
        Canvas.SetTop(SpinnerCanvas, center.Y - 16);
        SpinnerCanvas.Visibility = Visibility.Visible;
    }

    public void HideSpinner() => SpinnerCanvas.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Appends a text token to the response bubble and positions the bubble next to
    /// the cursor.  Called for each streaming token from Claude.
    /// </summary>
    public void AppendResponseToken(string token)
    {
        ResponseTextBlock.Text += token;
        PositionResponseBubbleNearCursor();
        ResponseBubble.Visibility = Visibility.Visible;
    }

    public void ClearResponseText()
    {
        ResponseTextBlock.Text = string.Empty;
        ResponseBubble.Visibility = Visibility.Collapsed;
        PointingLabelTextBlock.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Animates the cursor to a pointing target along a bezier arc.
    /// Coordinates are normalized [0,1] relative to the specified monitor bounds.
    /// </summary>
    public async Task AnimateCursorToPointingTargetAsync(
        PointingTarget pointingTarget,
        CancellationToken cancellationToken = default)
    {
        var monitorBounds = MonitorCoordinateMapper.GetMonitorBounds(pointingTarget.ZeroBasedMonitorIndex);
        if (monitorBounds == null) return;

        // Convert normalized coordinates to absolute screen pixels
        var targetScreenX = monitorBounds.Value.X + pointingTarget.NormalizedX * monitorBounds.Value.Width;
        var targetScreenY = monitorBounds.Value.Y + pointingTarget.NormalizedY * monitorBounds.Value.Height;

        // Convert screen pixels to WPF logical pixels (accounts for DPI scaling)
        var targetLogicalPoint = ScreenToLogical(new Point(targetScreenX, targetScreenY));

        // Show the pointing label
        PointingLabelTextBlock.Text = pointingTarget.Label;
        PointingLabelTextBlock.Visibility = Visibility.Visible;

        await AnimateCursorAlongBezierArcAsync(_cursorScreenPosition, targetLogicalPoint, cancellationToken);
    }

    public void ScheduleTransientFadeOut()
    {
        if (!_isTransientMode) return;

        CancelTransientFadeOut();

        _transientFadeOutTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TransientFadeOutDelay
        };
        _transientFadeOutTimer.Tick += (_, _) =>
        {
            _transientFadeOutTimer?.Stop();
            HideOverlay();
        };
        _transientFadeOutTimer.Start();
    }

    // ── Bezier arc cursor animation ───────────────────────────────────────────

    private async Task AnimateCursorAlongBezierArcAsync(
        Point startPoint,
        Point endPoint,
        CancellationToken cancellationToken)
    {
        // Control point for the bezier arc — offset upward so the cursor traces a gentle arc
        var controlPoint = new Point(
            (startPoint.X + endPoint.X) / 2,
            Math.Min(startPoint.Y, endPoint.Y) - 120);

        var animationDuration = DS.Animation.CursorTravelDuration;
        var stepCount = 60;
        var stepDelay = animationDuration / stepCount;

        for (int step = 0; step <= stepCount; step++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var t = (double)step / stepCount;
            var cursorPosition = EvaluateQuadraticBezier(startPoint, controlPoint, endPoint, t);

            Dispatcher.Invoke(() => PlaceCursorAt(cursorPosition));

            await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Point EvaluateQuadraticBezier(Point p0, Point p1, Point p2, double t)
    {
        var oneMinusT = 1 - t;
        return new Point(
            oneMinusT * oneMinusT * p0.X + 2 * oneMinusT * t * p1.X + t * t * p2.X,
            oneMinusT * oneMinusT * p0.Y + 2 * oneMinusT * t * p1.Y + t * t * p2.Y);
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private void PlaceCursorAt(Point logicalPosition)
    {
        _cursorScreenPosition = logicalPosition;
        Canvas.SetLeft(CursorCanvas, logicalPosition.X - 24); // center 48px cursor on point
        Canvas.SetTop(CursorCanvas, logicalPosition.Y - 24);
        PositionResponseBubbleNearCursor();
    }

    private void PositionResponseBubbleNearCursor()
    {
        // Place the bubble to the right of the cursor; flip left if it would overflow screen edge
        const double bubbleOffsetX = 56;
        const double bubbleOffsetY = -8;
        const double screenRightMargin = 16;

        var bubbleLeft = _cursorScreenPosition.X + bubbleOffsetX;
        var maxBubbleRight = ActualWidth - 400 - screenRightMargin;

        if (bubbleLeft > maxBubbleRight) bubbleLeft = _cursorScreenPosition.X - 400 - bubbleOffsetX;

        Canvas.SetLeft(ResponseBubble, bubbleLeft);
        Canvas.SetTop(ResponseBubble, _cursorScreenPosition.Y + bubbleOffsetY);
    }

    private void StretchToFullPrimaryScreen()
    {
        var primaryScreen = Screen.PrimaryScreen;
        if (primaryScreen == null) return;

        Left = primaryScreen.Bounds.X;
        Top = primaryScreen.Bounds.Y;
        Width = primaryScreen.Bounds.Width;
        Height = primaryScreen.Bounds.Height;
    }

    private Point GetOverlayCenterPoint() => new(ActualWidth / 2, ActualHeight / 2);

    private static Point GetCurrentMouseScreenPosition()
    {
        var cursorPosition = System.Windows.Forms.Cursor.Position;
        return new Point(cursorPosition.X, cursorPosition.Y);
    }

    private Point ScreenToLogical(Point screenPoint)
    {
        // Account for the overlay window's position when converting screen coords to canvas coords
        return new Point(screenPoint.X - Left, screenPoint.Y - Top);
    }

    // ── Fade animation ────────────────────────────────────────────────────────

    private void FadeOverlayOpacityTo(double targetOpacity, TimeSpan duration, Action? onCompleted = null)
    {
        var animation = new DoubleAnimation(targetOpacity, duration);
        if (onCompleted != null) animation.Completed += (_, _) => onCompleted();
        BeginAnimation(OpacityProperty, animation);
    }

    private void CancelTransientFadeOut()
    {
        _transientFadeOutTimer?.Stop();
        _transientFadeOutTimer = null;
    }
}

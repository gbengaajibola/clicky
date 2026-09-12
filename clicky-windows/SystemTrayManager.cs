using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace Clicky;

/// <summary>
/// Owns the Windows system tray icon (NotifyIcon) and the floating companion panel lifecycle.
/// Mirrors MenuBarPanelManager.swift — creates the tray icon, manages show/hide of the panel,
/// and installs a click-outside-to-dismiss handler.
/// </summary>
public sealed class SystemTrayManager : IDisposable
{
    private readonly CompanionManager _companionManager;
    private NotifyIcon? _notifyIcon;
    private CompanionPanelWindow? _companionPanelWindow;

    // Track whether the panel is currently visible so we can toggle it
    private bool _isPanelVisible;

    public SystemTrayManager(CompanionManager companionManager)
    {
        _companionManager = companionManager;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Clicky",
            Icon = LoadTrayIcon()
        };

        // Left-click toggles the floating panel; right-click shows a minimal context menu
        _notifyIcon.MouseClick += OnTrayIconMouseClick;

        var contextMenu = BuildContextMenu();
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    public void Dispose()
    {
        _companionPanelWindow?.Close();
        _companionPanelWindow = null;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    // ── Panel lifecycle ───────────────────────────────────────────────────────

    private void ShowPanel()
    {
        if (_companionPanelWindow == null || !_companionPanelWindow.IsLoaded)
        {
            _companionPanelWindow = new CompanionPanelWindow(_companionManager);

            // Auto-dismiss the panel when the user clicks outside it
            _companionPanelWindow.Deactivated += (_, _) => HidePanel();
        }

        PositionPanelNearTrayIcon();
        _companionPanelWindow.Show();
        _companionPanelWindow.Activate();
        _isPanelVisible = true;
    }

    private void HidePanel()
    {
        _companionPanelWindow?.Hide();
        _isPanelVisible = false;
    }

    private void PositionPanelNearTrayIcon()
    {
        if (_companionPanelWindow == null) return;

        // Place the panel above the taskbar, aligned to the right side of the primary screen
        var workArea = SystemParameters.WorkArea;
        var panelWidth = _companionPanelWindow.Width;
        var panelHeight = _companionPanelWindow.Height;

        // Snap to the bottom-right corner of the work area (above the system tray)
        _companionPanelWindow.Left = workArea.Right - panelWidth - DS.Spacing.Medium;
        _companionPanelWindow.Top = workArea.Bottom - panelHeight - DS.Spacing.Medium;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs mouseEventArgs)
    {
        if (mouseEventArgs.Button != MouseButtons.Left) return;

        // Toggle: if visible hide it, otherwise show it
        if (_isPanelVisible)
            HidePanel();
        else
            ShowPanel();
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var contextMenuStrip = new ContextMenuStrip();

        var showHideItem = new ToolStripMenuItem("Show / Hide Clicky");
        showHideItem.Click += (_, _) =>
        {
            if (_isPanelVisible) HidePanel(); else ShowPanel();
        };

        var quitItem = new ToolStripMenuItem("Quit Clicky");
        quitItem.Click += (_, _) =>
        {
            _notifyIcon!.Visible = false;
            System.Windows.Application.Current.Shutdown();
        };

        contextMenuStrip.Items.Add(showHideItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());
        contextMenuStrip.Items.Add(quitItem);

        return contextMenuStrip;
    }

    // ── Icon loading ──────────────────────────────────────────────────────────

    private static Icon LoadTrayIcon()
    {
        // Try to load a bundled .ico; fall back to a programmatically drawn blue circle icon
        const string icoResourcePath = "Resources/clicky-tray.ico";
        if (System.IO.File.Exists(icoResourcePath))
        {
            return new Icon(icoResourcePath);
        }

        return CreateFallbackTrayIcon();
    }

    private static Icon CreateFallbackTrayIcon()
    {
        // Draw a 32×32 blue circle as a fallback tray icon so the app stays usable even
        // without the .ico asset present in the build output
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillEllipse(Brushes.DodgerBlue, 2, 2, 28, 28);
        graphics.FillEllipse(Brushes.White, 10, 10, 12, 12);
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
}

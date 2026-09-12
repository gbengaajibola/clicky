using System.Windows;

namespace Clicky;

/// <summary>
/// Entry point. There is no main window — the app lives entirely in the system tray.
/// CompanionManager owns the full voice/AI pipeline; SystemTrayManager owns the tray icon
/// and floating panel lifecycle.
/// </summary>
public partial class App : Application
{
    private SystemTrayManager? _systemTrayManager;
    private CompanionManager? _companionManager;

    protected override async void OnStartup(StartupEventArgs startupEventArgs)
    {
        base.OnStartup(startupEventArgs);

        _companionManager = new CompanionManager();
        _systemTrayManager = new SystemTrayManager(_companionManager);

        _systemTrayManager.Initialize();

        await _companionManager.StartAsync();
    }

    protected override void OnExit(ExitEventArgs exitEventArgs)
    {
        _companionManager?.Dispose();
        _systemTrayManager?.Dispose();
        base.OnExit(exitEventArgs);
    }
}

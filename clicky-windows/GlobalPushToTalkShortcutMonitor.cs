using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clicky;

/// <summary>
/// Installs a system-wide low-level keyboard hook to detect Ctrl + Alt press/release.
/// This mirrors GlobalPushToTalkShortcutMonitor.swift — a listen-only hook so the app
/// detects the shortcut even when another window has focus.
///
/// The hook runs on a dedicated thread with a Windows message pump so it doesn't block
/// the WPF dispatcher.
/// </summary>
public sealed class GlobalPushToTalkShortcutMonitor : IDisposable
{
    // Raised when both Ctrl AND Alt are held simultaneously
    public event EventHandler? PushToTalkPressed;

    // Raised when either Ctrl or Alt is released while both were held
    public event EventHandler? PushToTalkReleased;

    // Win32 constants
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;   // Left Alt
    private const int VK_RMENU = 0xA5;   // Right Alt

    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc; // keep delegate alive to prevent GC
    private Thread? _hookThread;
    private uint _hookThreadId;

    private bool _isCtrlHeld;
    private bool _isAltHeld;
    private bool _isPttActive; // true while both Ctrl+Alt are held

    // ── Public API ────────────────────────────────────────────────────────────

    public void Start()
    {
        // The hook must be installed on a thread running a Windows message loop.
        // We spin a dedicated STA thread for exactly this purpose.
        _hookThread = new Thread(HookThreadEntryPoint)
        {
            IsBackground = true,
            Name = "PushToTalkHookThread"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose() => Stop();

    // ── Hook thread ───────────────────────────────────────────────────────────

    private void HookThreadEntryPoint()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();

        _keyboardProc = OnLowLevelKeyboardEvent;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _keyboardProc,
            NativeMethods.GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName),
            0);

        // Run the Windows message pump so hook callbacks are delivered to this thread
        NativeMethods.RunMessageLoop();
    }

    // ── Keyboard event processing ─────────────────────────────────────────────

    private IntPtr OnLowLevelKeyboardEvent(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var keyboardHookData = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var virtualKey = (int)keyboardHookData.vkCode;
            var isKeyDown = (int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN;
            var isKeyUp = (int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP;

            if (virtualKey == VK_LCONTROL || virtualKey == VK_RCONTROL)
            {
                _isCtrlHeld = isKeyDown;
            }
            else if (virtualKey == VK_LMENU || virtualKey == VK_RMENU)
            {
                _isAltHeld = isKeyDown;
            }

            var bothHeld = _isCtrlHeld && _isAltHeld;

            if (bothHeld && !_isPttActive)
            {
                _isPttActive = true;
                PushToTalkPressed?.Invoke(this, EventArgs.Empty);
            }
            else if (!bothHeld && _isPttActive && isKeyUp)
            {
                _isPttActive = false;
                PushToTalkReleased?.Invoke(this, EventArgs.Empty);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}

// ── Win32 P/Invoke declarations ───────────────────────────────────────────────

internal static class NativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    // Runs a Windows message loop on the calling thread until WM_QUIT is posted
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public System.Drawing.Point pt;
    }

    public static void RunMessageLoop()
    {
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }
}

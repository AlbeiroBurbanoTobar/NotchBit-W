using System.Runtime.InteropServices;

namespace NotchBit.Island;

/// <summary>The few Win32 calls WinUI does not wrap.</summary>
internal static partial class Win32
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_POPUP = 0x80000000;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_SYSMENU = 0x00080000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Turns the window into a bare popup: no caption, frame or system menu.</summary>
    public static void MakePopup(nint hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style = (style | WS_POPUP) & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
        SetWindowLongPtr(hwnd, GWL_STYLE, (nint)style);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_APPWINDOW = 0x00040000;

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;

    /// <summary>Raised when the monitor layout or a monitor's scale changes. Runs inside the window procedure.</summary>
    public static event Action? DisplayChanged;

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    /// <summary>Scale of the primary monitor (1.0 = 96 dpi), whichever monitor the window is on right now.</summary>
    public static double GetPrimaryMonitorScale()
    {
        nint monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
    }

    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>
    /// Whether an app is covering the screen (a fullscreen video or game, or a presentation),
    /// the same signal Windows uses to hold back notifications.
    /// </summary>
    public static bool IsFullscreenAppActive()
    {
        if (SHQueryUserNotificationState(out int state) != 0) return false;
        return state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }

    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    [DllImport("comctl32.dll")]
    public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint id, nuint refData);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    // Kept alive for the life of the process; the native side only holds a function pointer.
    private static readonly SubclassProc s_noFrameProc = NoFrameProc;

    /// <summary>
    /// Makes the whole window client area, so no frame is ever drawn. Restyling alone is not enough:
    /// the WinUI presenter puts the dialog frame back whenever it updates the window.
    /// </summary>
    public static void RemoveFrame(nint hwnd)
    {
        SetWindowSubclass(hwnd, s_noFrameProc, 1, 0);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private static nint NoFrameProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (msg == WM_NCCALCSIZE && wParam != 0) return 0;
        if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED) DisplayChanged?.Invoke();
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void AddExtendedStyle(nint hwnd, long add, long remove = 0)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = (style | add) & ~remove;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
    }
}

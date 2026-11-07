using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NotchBit.Island;
using Windows.Graphics;

namespace NotchBit.Settings;

internal static class WindowSizing
{
    /// <summary>Sizes a window in DIPs and centers it on the primary screen, at that screen's scale.</summary>
    public static void SizeAndCenter(Window window, double width, double height)
    {
        double scale = Win32.GetPrimaryMonitorScale();
        RectInt32 screen = DisplayArea.Primary.WorkArea;
        int w = Math.Min((int)Math.Round(width * scale), screen.Width);
        int h = Math.Min((int)Math.Round(height * scale), screen.Height);
        window.AppWindow.MoveAndResize(new RectInt32(
            screen.X + (screen.Width - w) / 2,
            screen.Y + (screen.Height - h) / 2,
            w, h));
    }

    /// <summary>The app icon, for the title bar and the taskbar.</summary>
    public static void SetAppIcon(Window window) =>
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NotchBit.ico"));
}

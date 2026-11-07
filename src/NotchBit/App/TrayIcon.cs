using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NotchBit.Shell;

/// <summary>The notification-area icon: the app's only entry point besides the island itself.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(Action onQuit)
    {
        var menu = new MenuFlyout();
        var quit = new MenuFlyoutItem { Text = "Salir de NotchBit" };
        quit.Click += (_, _) => onQuit();
        menu.Items.Add(quit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "NotchBit",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "NotchBit.ico"))),
            ContextFlyout = menu,
            // The menu is shown in its own small window, which is what works without a visible main window.
            ContextMenuMode = ContextMenuMode.SecondWindow,
        };
        _icon.ForceCreate();
    }

    public void Dispose() => _icon.Dispose();
}

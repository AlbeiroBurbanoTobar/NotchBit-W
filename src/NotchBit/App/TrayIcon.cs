using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NotchBit.Shell;

/// <summary>The notification-area icon: the app's way in besides the island itself.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(Action onSettings, Action onRecenter, Action onAbout, Action onQuit)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Ajustes…", onSettings));
        menu.Items.Add(Item("Centrar la isla", onRecenter));
        menu.Items.Add(Item("Acerca de NotchBit", onAbout));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Salir de NotchBit", onQuit));

        _icon = new TaskbarIcon
        {
            ToolTipText = "NotchBit",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "NotchBit.ico"))),
            ContextFlyout = menu,
            // The menu is shown in its own small window, which is what works without a visible main window.
            ContextMenuMode = ContextMenuMode.SecondWindow,
            // A plain click on the icon opens the settings, as most tray apps do.
            LeftClickCommand = new ActionCommand(onSettings),
        };
        _icon.ForceCreate();
    }

    public void Dispose() => _icon.Dispose();

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private sealed class ActionCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}

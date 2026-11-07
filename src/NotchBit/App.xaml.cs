using Microsoft.UI.Xaml;
using NotchBit.Core;
using NotchBit.Island;
using NotchBit.Modules;
using NotchBit.Shell;

namespace NotchBit;

public partial class App : Application
{
    private IslandWindow? _island;
    private TrayIcon? _tray;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Stand-ins until the real modules land; each one replaces its placeholder.
        var registry = new ModuleRegistry(
            [
                new PlaceholderModule("music", "Música", "", "Lo que suena ahora, con controles. Llega en la fase 3."),
                new PlaceholderModule("shelf", "Shelf", "", "Suelta archivos aquí para tenerlos a mano.", acceptsFileDrops: true),
            ],
            new SettingsStore());

        _island = new IslandWindow(registry);
        _island.ShowWithoutActivating();

        _tray = new TrayIcon(onQuit: Quit);
    }

    private void Quit()
    {
        _tray?.Dispose();
        _island?.Close();
        Exit();
    }
}

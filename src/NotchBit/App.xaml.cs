using Microsoft.UI.Xaml;
using NotchBit.Core;
using NotchBit.Island;
using NotchBit.Modules.Music;
using NotchBit.Modules.Shelf;
using NotchBit.Settings;
using NotchBit.Shell;

namespace NotchBit;

public partial class App : Application
{
    private ModuleRegistry? _registry;
    private IslandWindow? _island;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private AboutWindow? _about;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = new SettingsStore();
        _registry = new ModuleRegistry(
            [
                new MusicModule(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()),
                new ShelfModule(),
            ],
            settings);

        _island = new IslandWindow(_registry, settings);
        _island.ShowWithoutActivating();

        _tray = new TrayIcon(onSettings: ShowSettings, onRecenter: () => _island?.ResetPosition(), onAbout: ShowAbout, onQuit: Quit);

        // Handy for a shortcut, and for checking a window without going through the tray.
        string[] commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Contains("--settings")) ShowSettings();
        if (commandLine.Contains("--about")) ShowAbout();
    }

    private void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(_registry!);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Activate();
    }

    private void ShowAbout()
    {
        if (_about is null)
        {
            _about = new AboutWindow();
            _about.Closed += (_, _) => _about = null;
        }
        _about.Activate();
    }

    private void Quit()
    {
        _tray?.Dispose();
        _settings?.Close();
        _about?.Close();
        _island?.Close();
        Exit();
    }
}

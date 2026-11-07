using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchBit.Core;
using NotchBit.Shell;

namespace NotchBit.Settings;

/// <summary>Launch at login, and which modules show up in the island.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ModuleRegistry _registry;
    /// <summary>True while the code, not the user, sets a switch.</summary>
    private bool _isLoading;

    public SettingsWindow(ModuleRegistry registry)
    {
        _registry = registry;
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        WindowSizing.SetAppIcon(this);
        WindowSizing.SizeAndCenter(this, 520, 560);

        _isLoading = true;
        StartupSwitch.IsOn = StartupRegistration.IsEnabled;
        foreach (IIslandModule module in registry.AllModules) ModulesPanel.Children.Add(BuildModuleRow(module));
        _isLoading = false;
    }

    private Border BuildModuleRow(IIslandModule module)
    {
        var toggle = new ToggleSwitch
        {
            Header = module.Title,
            OnContent = "Activado",
            OffContent = "Desactivado",
            IsOn = _registry.IsEnabled(module.Id),
        };
        toggle.Toggled += (_, _) =>
        {
            if (!_isLoading) _registry.SetEnabled(module.Id, toggle.IsOn);
        };

        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = module.Glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = row };
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        try
        {
            StartupRegistration.SetEnabled(StartupSwitch.IsOn);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Group policy can lock the Run key; put the switch back to what is really set.
            _isLoading = true;
            StartupSwitch.IsOn = StartupRegistration.IsEnabled;
            _isLoading = false;
        }
    }
}

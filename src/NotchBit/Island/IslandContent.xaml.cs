using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using NotchBit.Core;

namespace NotchBit.Island;

/// <summary>What the open island shows: a tab per enabled module, and the selected module's page.</summary>
public sealed partial class IslandContent : UserControl
{
    private readonly ModuleRegistry _registry;
    private string? _selectedId;

    public IslandContent(ModuleRegistry registry)
    {
        InitializeComponent();
        _registry = registry;
        _registry.Changed += Rebuild;
        Rebuild();
    }

    /// <summary>What the user drags to move the island; the window handles the moving.</summary>
    public UIElement MoveHandle => Grip;

    /// <summary>Switches to a module's tab, for example the one that just received a file.</summary>
    public void Select(IIslandModule module)
    {
        _selectedId = module.Id;
        Rebuild();
    }

    private void Rebuild()
    {
        IReadOnlyList<IIslandModule> modules = _registry.EnabledModules;
        Tabs.Children.Clear();

        if (modules.Count == 0)
        {
            Body.Content = new TextBlock { Text = "No hay módulos activados.", Opacity = 0.6 };
            return;
        }

        // The selected module may have been turned off since last time.
        IIslandModule selected = modules.FirstOrDefault(m => m.Id == _selectedId) ?? modules[0];
        _selectedId = selected.Id;

        foreach (IIslandModule module in modules)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(new FontIcon { Glyph = module.Glyph, FontSize = 14 });
            label.Children.Add(new TextBlock { Text = module.Title });

            var tab = new ToggleButton
            {
                Content = label,
                IsChecked = module == selected,
                Tag = module,
            };
            tab.Click += Tab_Click;
            Tabs.Children.Add(tab);
        }

        Body.Content = selected.CreateView();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        var module = (IIslandModule)((ToggleButton)sender).Tag;
        _selectedId = module.Id;
        Rebuild();
    }
}

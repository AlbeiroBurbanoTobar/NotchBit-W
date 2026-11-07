using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotchBit.Core;

namespace NotchBit.Modules;

/// <summary>
/// A stand-in page for a module that is not built yet. It also exercises the module contract:
/// when it accepts file drops, it lists what was dropped on it.
/// </summary>
public sealed class PlaceholderModule(
    string id, string title, string glyph, string description, bool acceptsFileDrops = false) : IIslandModule
{
    private readonly ObservableCollection<string> _files = [];
    private UIElement? _view;

    public string Id => id;
    public string Title => title;
    public string Glyph => glyph;
    public bool AcceptsFileDrops => acceptsFileDrops;

    public UIElement CreateView() => _view ??= BuildView();

    public void ReceiveFiles(IReadOnlyList<string> paths)
    {
        foreach (string path in paths) _files.Add(path);
    }

    private StackPanel BuildView()
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4, 8, 4, 0) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        if (acceptsFileDrops)
            panel.Children.Add(new ItemsControl { ItemsSource = _files, Opacity = 0.9 });
        return panel;
    }
}

using Microsoft.UI.Xaml;
using NotchBit.Core;

namespace NotchBit.Modules.Shelf;

/// <summary>A place to keep files within reach: drop them on the island, drag them out as copies.</summary>
public sealed class ShelfModule : IIslandModule
{
    private readonly ShelfController _controller = new();
    private ShelfView? _view;

    public string Id => "shelf";
    public string Title => "Shelf";
    public string Glyph => "";

    public bool AcceptsFileDrops => true;

    public UIElement CreateView() => _view ??= new ShelfView(_controller);

    public void ReceiveFiles(IReadOnlyList<string> paths) => _controller.Add(paths);
}

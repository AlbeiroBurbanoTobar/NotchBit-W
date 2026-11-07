using Microsoft.UI.Xaml;

namespace NotchBit.Core;

/// <summary>A feature that shows up as a tab inside the open island (music, shelf, ...).</summary>
public interface IIslandModule
{
    /// <summary>Stable identifier, also used to persist whether the module is turned off.</summary>
    string Id { get; }
    string Title { get; }
    /// <summary>A glyph from Segoe Fluent Icons, shown on the module's tab.</summary>
    string Glyph { get; }

    /// <summary>The module's page. Called every time the tab is selected, so cache it if it keeps state.</summary>
    UIElement CreateView();

    /// <summary>Whether the module has something to show beside the resting island (for example, music playing).</summary>
    bool HasCompactActivity => false;
    /// <summary>Raised on the UI thread when <see cref="HasCompactActivity"/> changes.</summary>
    event Action? CompactActivityChanged { add { } remove { } }
    /// <summary>Content shown left of the resting island while <see cref="HasCompactActivity"/> is true.</summary>
    UIElement? CreateCompactLeading() => null;
    /// <summary>Content shown right of the resting island while <see cref="HasCompactActivity"/> is true.</summary>
    UIElement? CreateCompactTrailing() => null;

    /// <summary>Whether files dropped on the island go to this module.</summary>
    bool AcceptsFileDrops => false;
    void ReceiveFiles(IReadOnlyList<string> paths) { }
}

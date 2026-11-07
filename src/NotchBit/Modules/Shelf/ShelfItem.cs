using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NotchBit.Modules.Shelf;

/// <summary>A file or folder kept on the shelf. It is a reference: the original is never moved or copied.</summary>
public sealed class ShelfItem : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;

    public ShelfItem(string path, DateTimeOffset addedAt)
    {
        Path = path;
        AddedAt = addedAt;
        Refresh();
    }

    public string Path { get; }
    public DateTimeOffset AddedAt { get; }

    /// <summary>The file name, or the full path for a drive root, which has no name.</summary>
    public string Name
    {
        get
        {
            string name = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
            return name.Length > 0 ? name : Path;
        }
    }

    public bool IsFolder { get; private set; }
    public bool IsMissing { get; private set; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            Raise(nameof(Thumbnail), nameof(FallbackVisibility));
        }
    }

    private bool _isSelected;
    private bool _isHovered;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            Raise(nameof(IsSelected), nameof(TileBackground));
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered == value) return;
            _isHovered = value;
            Raise(nameof(IsHovered), nameof(TileBackground));
        }
    }

    // Transparent, not null: a tile without a background cannot be hit, so clicks would fall through it.
    private static readonly Brush ClearBrush = new SolidColorBrush(Colors.Transparent);
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SelectedBrush = new SolidColorBrush(AccentWithAlpha(0x66));

    public Brush TileBackground => _isSelected ? SelectedBrush : _isHovered ? HoverBrush : ClearBrush;

    internal static Color AccentWithAlpha(byte alpha)
    {
        Color accent = Application.Current.Resources.TryGetValue("SystemAccentColor", out object? value) && value is Color c
            ? c
            : Color.FromArgb(0xFF, 0x4C, 0xA0, 0xE8);
        return Color.FromArgb(alpha, accent.R, accent.G, accent.B);
    }

    // The tile binds to these, so it needs no converters.
    public Visibility FallbackVisibility => _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    public string FallbackGlyph => IsFolder ? "" : "";
    public double TileOpacity => IsMissing ? 0.4 : 1;
    public string ToolTip => IsMissing ? $"{Path}\nNo se encuentra" : Path;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Checks whether the file is still there; files get moved and deleted behind the shelf's back.</summary>
    public void Refresh()
    {
        bool isFolder = Directory.Exists(Path);
        bool isMissing = !isFolder && !File.Exists(Path);
        if (isFolder == IsFolder && isMissing == IsMissing) return;

        IsFolder = isFolder;
        IsMissing = isMissing;
        Raise(nameof(IsFolder), nameof(IsMissing), nameof(FallbackGlyph), nameof(TileOpacity), nameof(ToolTip));
    }

    private void Raise(params string[] names)
    {
        foreach (string name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

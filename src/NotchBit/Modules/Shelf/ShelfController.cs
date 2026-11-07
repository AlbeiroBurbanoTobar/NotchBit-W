using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace NotchBit.Modules.Shelf;

/// <summary>The files on the shelf, kept as references in %LOCALAPPDATA%\NotchBit\shelf.json.</summary>
public sealed class ShelfController
{
    private sealed record Entry(string Path, DateTimeOffset AddedAt);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public ShelfController(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchBit", "shelf.json");

        foreach (Entry entry in Load())
        {
            // The file lives in the user's profile and anything running as the user can edit it,
            // so take only what the shelf itself would have saved, once each.
            if (!IsAcceptablePath(entry.Path)
                || Items.Any(i => string.Equals(i.Path, entry.Path, StringComparison.OrdinalIgnoreCase))) continue;

            var item = new ShelfItem(entry.Path, entry.AddedAt);
            Items.Add(item);
            _ = LoadThumbnailAsync(item);
        }
    }

    public ObservableCollection<ShelfItem> Items { get; } = [];

    /// <summary>Adds files and folders, skipping ones already on the shelf. Returns how many were added.</summary>
    public int Add(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string raw in paths)
        {
            string path;
            try
            {
                path = System.IO.Path.GetFullPath(raw);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!IsAcceptablePath(path) || (!File.Exists(path) && !Directory.Exists(path))) continue;
            if (Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) continue;

            var item = new ShelfItem(path, DateTimeOffset.Now);
            Items.Add(item);
            _ = LoadThumbnailAsync(item);
            added++;
        }

        if (added > 0) Save();
        return added;
    }

    /// <summary>
    /// A plain, absolute path to a file or folder, local or on a network share. Rejected: relative
    /// paths, which would resolve against wherever the app happens to start and could point at a
    /// program; device paths (<c>\\?\</c>, <c>\\.\</c>); and characters Windows never allows in a
    /// name, which only a hand-edited file would contain and which could break the Explorer command line.
    /// </summary>
    internal static bool IsAcceptablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32_767) return false;
        if (!System.IO.Path.IsPathFullyQualified(path)) return false;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        if (path.IndexOfAny(['"', '<', '>', '|', '*', '?']) >= 0) return false;
        return !path.Any(char.IsControl);
    }

    public void Remove(IEnumerable<ShelfItem> items)
    {
        bool removed = false;
        foreach (ShelfItem item in items.ToList()) removed |= Items.Remove(item);
        if (removed) Save();
    }

    public void Clear()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        Save();
    }

    /// <summary>Re-checks every item against the disk.</summary>
    public void RefreshAll()
    {
        foreach (ShelfItem item in Items) item.Refresh();
    }

    /// <summary>The shell fails some thumbnail requests that overlap, so they go one at a time.</summary>
    private static readonly SemaphoreSlim ThumbnailGate = new(1);

    /// <summary>The shell's own icon or preview, so a photo shows the photo and a PDF the PDF icon.</summary>
    private static async Task LoadThumbnailAsync(ShelfItem item)
    {
        if (item.IsMissing) return;
        await ThumbnailGate.WaitAsync();
        try
        {
            IStorageItemProperties source = item.IsFolder
                ? await StorageFolder.GetFolderFromPathAsync(item.Path)
                : await StorageFile.GetFileFromPathAsync(item.Path);
            using StorageItemThumbnail? thumbnail = await source.GetThumbnailAsync(ThumbnailMode.SingleItem, 96);
            if (thumbnail is null || thumbnail.Size == 0) return;

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            item.Thumbnail = image;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No preview available; the tile keeps its generic icon.
        }
        finally
        {
            ThumbnailGate.Release();
        }
    }

    private List<Entry> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file just means an empty shelf.
        }
        return [];
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var entries = Items.Select(i => new Entry(i.Path, i.AddedAt));
            File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing the list is better than crashing the island.
        }
    }
}

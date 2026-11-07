using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace NotchBit.Modules.Shelf;

/// <summary>The Shelf tab: a grid of the files kept on the shelf.</summary>
public sealed partial class ShelfView : UserControl
{
    private readonly ShelfController _controller;

    public ShelfView(ShelfController controller)
    {
        InitializeComponent();
        _controller = controller;
        Repeater.ItemsSource = controller.Items;

        Marquee.Fill = new SolidColorBrush(ShelfItem.AccentWithAlpha(0x33));
        Marquee.Stroke = new SolidColorBrush(ShelfItem.AccentWithAlpha(0xCC));
        // The scroller marks pointer events handled, so ask to hear about them anyway.
        TileScroller.AddHandler(PointerPressedEvent, new PointerEventHandler(Scroller_PointerPressed), handledEventsToo: true);
        TileScroller.AddHandler(PointerMovedEvent, new PointerEventHandler(Scroller_PointerMoved), handledEventsToo: true);
        TileScroller.AddHandler(PointerReleasedEvent, new PointerEventHandler(Scroller_PointerEnded), handledEventsToo: true);
        TileScroller.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Scroller_PointerEnded), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            _controller.Items.CollectionChanged -= OnItemsChanged;
            _controller.Items.CollectionChanged += OnItemsChanged;
            _controller.RefreshAll();
            UpdateHeader();
        };
        Unloaded += (_, _) => _controller.Items.CollectionChanged -= OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateHeader();

    private void UpdateHeader()
    {
        int count = _controller.Items.Count;
        Header.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
        int selected = _controller.Items.Count(i => i.IsSelected);
        CountText.Text = selected > 0
            ? $"{selected} de {count} seleccionados"
            : count == 1 ? "1 elemento" : $"{count} elementos";
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _controller.Clear();

    // ---- Tiles ----

    /// <summary>
    /// Drags the file out as a copy: the target gets its own copy and the original stays put.
    /// The file is read lazily, so the drag starts only once the storage item is ready.
    /// </summary>
    private async void Tile_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (ItemOf(sender) is not { } dragged)
        {
            args.Cancel = true;
            return;
        }

        // Dragging a tile that is part of the selection takes the whole selection; dragging one
        // outside it takes just that one, as in Explorer.
        if (!dragged.IsSelected) SelectOnly(dragged);
        List<ShelfItem> items = _controller.Items.Where(i => i.IsSelected && !i.IsMissing).ToList();
        if (items.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        args.AllowedOperations = DataPackageOperation.Copy;
        args.Data.RequestedOperation = DataPackageOperation.Copy;

        DragOperationDeferral deferral = args.GetDeferral();
        try
        {
            var storageItems = new List<IStorageItem>();
            foreach (ShelfItem item in items)
            {
                try
                {
                    storageItems.Add(item.IsFolder
                        ? await StorageFolder.GetFolderFromPathAsync(item.Path)
                        : await StorageFile.GetFileFromPathAsync(item.Path));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    // Gone or locked since the last check: leave it out and take the rest.
                    item.Refresh();
                }
            }

            if (storageItems.Count == 0)
            {
                args.Cancel = true;
                return;
            }
            args.Data.SetStorageItems(storageItems, readOnly: true);
            await SetDragPreviewAsync(args.DragUI, items);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Shows what is being dragged under the pointer: up to three icons fanned out, a count badge
    /// for several files, and the name (or the number of items) underneath.
    /// </summary>
    private async Task SetDragPreviewAsync(DragUI dragUI, IReadOnlyList<ShelfItem> items)
    {
        PreviewIcons.Children.Clear();
        int shown = Math.Min(items.Count, 3);
        // Back to front, so the first item ends up on top.
        for (int i = shown - 1; i >= 0; i--)
        {
            ShelfItem item = items[i];
            FrameworkElement icon = item.Thumbnail is { } thumbnail
                ? new Image { Source = thumbnail, Width = 44, Height = 44, Stretch = Stretch.Uniform }
                : new FontIcon { Glyph = item.FallbackGlyph, FontSize = 36 };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.RenderTransform = new TranslateTransform { X = (i - (shown - 1) / 2.0) * 10, Y = -i * 4 };
            PreviewIcons.Children.Add(icon);
        }

        if (items.Count > 1)
        {
            PreviewIcons.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 20,
                Height = 20,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(ShelfItem.AccentWithAlpha(0xFF)),
                Child = new TextBlock
                {
                    Text = items.Count.ToString(),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        PreviewCaption.Text = items.Count == 1 ? items[0].Name : $"{items.Count} elementos";
        DragPreview.UpdateLayout();

        try
        {
            var render = new RenderTargetBitmap();
            await render.RenderAsync(DragPreview);
            IBuffer pixels = await render.GetPixelsAsync();
            SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                pixels, BitmapPixelFormat.Bgra8, render.PixelWidth, render.PixelHeight, BitmapAlphaMode.Premultiplied);
            // Hold the card by the middle of its top edge, so it hangs below the pointer and Windows'
            // own "Copy" label, drawn above the pointer, does not cover it.
            dragUI.SetContentFromSoftwareBitmap(bitmap, new Point(render.PixelWidth / 2.0, 0));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // Without a preview the drag still works; Windows shows its default cursor.
        }
    }

    // ---- Selection rectangle ----

    /// <summary>How far the pointer must move before a press on empty space becomes a selection rectangle.</summary>
    private const double MarqueeThreshold = 4;

    private Point? _marqueeStart;
    private bool _marqueeMoved;
    /// <summary>What was selected before the rectangle started (with Ctrl held), kept while it changes.</summary>
    private HashSet<ShelfItem> _marqueeBase = [];

    private void Scroller_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsOnTileOrScrollBar(e.OriginalSource as DependencyObject)) return;
        PointerPoint point = e.GetCurrentPoint(MarqueeLayer);
        if (!point.Properties.IsLeftButtonPressed) return;

        _marqueeStart = point.Position;
        _marqueeMoved = false;
        _marqueeBase = IsDown(VirtualKey.Control) ? [.. _controller.Items.Where(i => i.IsSelected)] : [];
        TileScroller.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Scroller_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_marqueeStart is not { } start) return;
        Point current = e.GetCurrentPoint(MarqueeLayer).Position;

        if (!_marqueeMoved)
        {
            if (Math.Abs(current.X - start.X) < MarqueeThreshold && Math.Abs(current.Y - start.Y) < MarqueeThreshold) return;
            _marqueeMoved = true;
            Marquee.Visibility = Visibility.Visible;
        }

        var rect = new Rect(start, current);
        Canvas.SetLeft(Marquee, rect.X);
        Canvas.SetTop(Marquee, rect.Y);
        Marquee.Width = rect.Width;
        Marquee.Height = rect.Height;
        SelectTilesIn(rect);
    }

    private void Scroller_PointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (_marqueeStart is null) return;

        // A click on empty space, without dragging, clears the selection (unless Ctrl is held).
        if (!_marqueeMoved && !IsDown(VirtualKey.Control))
        {
            foreach (ShelfItem item in _controller.Items) item.IsSelected = false;
        }

        _marqueeStart = null;
        _anchor = null;
        Marquee.Visibility = Visibility.Collapsed;
        TileScroller.ReleasePointerCaptures();
        UpdateHeader();
    }

    private void SelectTilesIn(Rect rect)
    {
        for (int i = 0; i < _controller.Items.Count; i++)
        {
            ShelfItem item = _controller.Items[i];
            bool touched = false;
            if (Repeater.TryGetElement(i) is FrameworkElement tile)
            {
                Rect bounds = tile.TransformToVisual(MarqueeLayer)
                    .TransformBounds(new Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
                touched = bounds.X < rect.X + rect.Width && bounds.X + bounds.Width > rect.X
                    && bounds.Y < rect.Y + rect.Height && bounds.Y + bounds.Height > rect.Y;
            }
            item.IsSelected = touched || _marqueeBase.Contains(item);
        }
        UpdateHeader();
    }

    /// <summary>Presses on a tile start a file drag, and presses on the scroll bar scroll; neither draws a rectangle.</summary>
    private bool IsOnTileOrScrollBar(DependencyObject? element)
    {
        for (DependencyObject? e = element; e is not null && e != TileScroller; e = VisualTreeHelper.GetParent(e))
        {
            if (e is FrameworkElement { Tag: ShelfItem } || e is ScrollBar) return true;
        }
        return false;
    }

    // ---- Selection ----

    /// <summary>The tile a Shift-click range starts from.</summary>
    private ShelfItem? _anchor;

    private void Tile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true; // otherwise the empty-space handler below clears the selection
        if (ItemOf(sender) is not { } item) return;

        bool ctrl = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);

        if (shift && _anchor is not null && _controller.Items.Contains(_anchor))
        {
            // Everything between the anchor and this tile; with Ctrl, added to what is selected.
            int from = _controller.Items.IndexOf(_anchor);
            int to = _controller.Items.IndexOf(item);
            (from, to) = (Math.Min(from, to), Math.Max(from, to));
            for (int i = 0; i < _controller.Items.Count; i++)
            {
                bool inRange = i >= from && i <= to;
                if (inRange) _controller.Items[i].IsSelected = true;
                else if (!ctrl) _controller.Items[i].IsSelected = false;
            }
        }
        else if (ctrl)
        {
            item.IsSelected = !item.IsSelected;
            _anchor = item;
        }
        else
        {
            SelectOnly(item);
            _anchor = item;
        }
        UpdateHeader();
    }

    private void SelectOnly(ShelfItem selected)
    {
        foreach (ShelfItem item in _controller.Items) item.IsSelected = item == selected;
        UpdateHeader();
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Tile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Open(item);
    }

    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) item.IsHovered = true;
    }

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) item.IsHovered = false;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is { } item) Open(item);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is not { } item) return;
        // Windows opens the containing folder with the file selected.
        TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (TagOf(sender) is not { } item) return;
        // On a selected tile, "remove" means the whole selection.
        _controller.Remove(item.IsSelected ? _controller.Items.Where(i => i.IsSelected).ToList() : [item]);
    }

    private static void Open(ShelfItem item)
    {
        item.Refresh();
        if (item.IsMissing) return;
        TryStart(new ProcessStartInfo(item.Path) { UseShellExecute = true });
    }

    private static void TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // No app is associated with the file type, or the user cancelled the "open with" prompt.
        }
    }

    // ItemsRepeater does not set DataContext on its tiles, so each one carries its item in Tag.
    private static ShelfItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as ShelfItem;
    private static ShelfItem? TagOf(object sender) => (sender as FrameworkElement)?.Tag as ShelfItem;
}

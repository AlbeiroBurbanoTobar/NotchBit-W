using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NotchBit.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;

namespace NotchBit.Island;

/// <summary>
/// The floating "island" at the top center of the primary screen.
/// At rest it is a thin white line, like the gesture bar on a phone, or a small black pill while a
/// module (music, say) has something to show. On hover it grows into the black island.
/// At rest the window is only slightly larger than what is drawn, so it never blocks clicks
/// on whatever is around it.
/// </summary>
public sealed partial class IslandWindow : Window
{
    /// <summary>How the island looks when the pointer is not on it.</summary>
    private readonly record struct RestState(
        Size Pill, Size Window, Color Color, CornerRadius Corners, double OffsetY, double CompactOpacity);

    // The line. The window around it is bigger than the line so it is easy to reach.
    private static readonly Size LineSize = new(72, 4);
    private static readonly Size LineWindowSize = new(160, 14);
    private const double LineOffsetY = 4;
    private static readonly Color LineColor = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
    private static readonly CornerRadius LineCorners = new(2);

    // The small black pill with a module's compact content on its sides.
    private static readonly Size CompactSize = new(240, 32);
    private static readonly Size CompactWindowSize = new(260, 36);
    private static readonly CornerRadius CompactCorners = new(0, 0, 14, 14);

    // Keep in step with ContentHost's size in IslandWindow.xaml.
    private static readonly Size OpenSize = new(480, 210);
    private static readonly Color OpenColor = Colors.Black;
    private static readonly CornerRadius OpenCorners = new(0, 0, 20, 20);

    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(260);
    /// <summary>Grace period after the pointer leaves, so brushing past the edge does not close it.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(250);

    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _closeTimer;
    private readonly DispatcherQueueTimer _fullscreenTimer;
    private readonly DispatcherQueueTimer _pointerWatch;
    private readonly ModuleRegistry _registry;
    private readonly SettingsStore _settings;
    private readonly IslandContent _content;
    private Storyboard? _storyboard;
    private Size _windowSize = LineWindowSize;
    private IIslandModule? _compactModule;
    private bool _isOpen;
    private bool _isHiddenForFullscreen;
    /// <summary>Where along the top edge the island sits; <c>null</c> is the center of the primary monitor.</summary>
    private IslandPlacement? _placement;
    private bool _isMoving;
    /// <summary>How far from the window's center the user grabbed it, in pixels, so it does not jump.</summary>
    private int _grabOffsetX;

    public IslandWindow(ModuleRegistry registry, SettingsStore settings)
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        _registry = registry;
        _settings = settings;
        _placement = settings.Island;
        _content = new IslandContent(registry);
        ContentHost.Children.Add(_content);

        UIElement handle = _content.MoveHandle;
        handle.PointerPressed += Grip_PointerPressed;
        handle.PointerMoved += Grip_PointerMoved;
        handle.PointerReleased += Grip_PointerEnded;
        handle.PointerCaptureLost += Grip_PointerEnded;
        handle.DoubleTapped += (_, _) => ResetPosition();

        SystemBackdrop = new TransparentTintBackdrop();
        ConfigureChrome();

        _closeTimer = DispatcherQueue.CreateTimer();
        _closeTimer.Interval = CloseDelay;
        _closeTimer.IsRepeating = false;
        _closeTimer.Tick += (_, _) =>
        {
            // A pressed button means a drag or a slider scrub is under way; closing now would cut it short.
            if (Win32.IsLeftButtonDown() || HasOpenPopup()) _closeTimer.Start();
            // The pointer can be back over the island by now, for example after picking from a menu.
            else if (!IsPointerInside()) SetOpen(false);
        };

        // Pointer events stop during a drag, and a file dragged out of the island never reports the
        // pointer leaving, so while open, also check where the pointer is.
        _pointerWatch = DispatcherQueue.CreateTimer();
        _pointerWatch.Interval = TimeSpan.FromMilliseconds(300);
        _pointerWatch.Tick += (_, _) => CloseIfPointerLeft();
        _pointerWatch.Start();

        UpdateCompactModule();
        ApplyRestState();
        PlaceWindow(Rest().Window);

        _registry.Changed += OnModulesChanged;
        _registry.CompactActivityChanged += OnModulesChanged;

        // Monitors added or removed, resolution or scale changed: stay where the user put the island,
        // or fall back to the primary monitor if that one is gone. Deferred, since this fires inside
        // the window procedure.
        Win32.DisplayChanged += OnDisplayChanged;

        // There is no notification for "an app went fullscreen", so check a few times a second.
        _fullscreenTimer = DispatcherQueue.CreateTimer();
        _fullscreenTimer.Interval = TimeSpan.FromMilliseconds(500);
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenState();
        _fullscreenTimer.Start();

        Closed += (_, _) =>
        {
            Win32.DisplayChanged -= OnDisplayChanged;
            _registry.Changed -= OnModulesChanged;
            _registry.CompactActivityChanged -= OnModulesChanged;
            _fullscreenTimer.Stop();
            _pointerWatch.Stop();
        };
    }

    /// <summary>A context menu or flyout is its own window, so pointing at it counts as leaving the island.</summary>
    private bool HasOpenPopup() =>
        Content?.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;

    private void CloseIfPointerLeft()
    {
        if (!_isOpen || Win32.IsLeftButtonDown() || HasOpenPopup() || IsPointerInside()) return;

        _closeTimer.Stop();
        SetOpen(false);
    }

    /// <summary>Whether the mouse is over the island's window, going by screen position rather than pointer events.</summary>
    private bool IsPointerInside()
    {
        if (!Win32.TryGetCursorPosition(out int x, out int y)) return false;

        PointInt32 origin = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        return x >= origin.X && x < origin.X + size.Width && y >= origin.Y && y < origin.Y + size.Height;
    }

    public void ShowWithoutActivating() => AppWindow.Show(activateWindow: false);

    private void ConfigureChrome()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        // Not in the taskbar or Alt+Tab, and clicking it does not steal focus from the active app.
        AppWindow.IsShownInSwitchers = false;
        Win32.AddExtendedStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, remove: Win32.WS_EX_APPWINDOW);
        // The presenter leaves a dialog frame behind (a 3px light edge); make it a frameless popup.
        Win32.MakePopup(_hwnd);
        Win32.RemoveFrame(_hwnd);

        // Windows 11 rounds every top-level window and draws a 1px frame; the pill draws its own shape.
        int corner = Win32.DWMWCP_DONOTROUND;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int border = Win32.DWMWA_COLOR_NONE;
        Win32.DwmSetWindowAttribute(_hwnd, Win32.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private RestState Rest() => _compactModule is null
        ? new RestState(LineSize, LineWindowSize, LineColor, LineCorners, LineOffsetY, CompactOpacity: 0)
        : new RestState(CompactSize, CompactWindowSize, Colors.Black, CompactCorners, OffsetY: 0, CompactOpacity: 1);

    private void ApplyRestState()
    {
        RestState rest = Rest();
        Pill.Width = rest.Pill.Width;
        Pill.Height = rest.Pill.Height;
        Pill.CornerRadius = rest.Corners;
        PillBrush.Color = rest.Color;
        PillShift.Y = rest.OffsetY;
        CompactHost.Opacity = rest.CompactOpacity;
        CompactHost.Visibility = rest.CompactOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Sizes the window to <paramref name="size"/> (in DIPs) and puts it at the top of its monitor,
    /// centered on the island's place along that edge.
    /// </summary>
    private void PlaceWindow(Size size)
    {
        _windowSize = size;
        RectInt32 screen = CurrentScreen();
        // Use that monitor's own scale, not the window's: right after a move or a monitor change
        // the window can still carry the old monitor's DPI.
        double scale = ScaleOf(screen);

        int width = (int)Math.Round(size.Width * scale);
        int height = (int)Math.Round(size.Height * scale);
        int center = screen.X + (int)Math.Round((_placement?.CenterFraction ?? 0.5) * screen.Width);
        int x = Math.Clamp(center - width / 2, screen.X, screen.X + screen.Width - width);
        AppWindow.MoveAndResize(new RectInt32(x, screen.Y, width, height));
    }

    /// <summary>The monitor the island lives on: the one it was moved to if still connected, else the primary.</summary>
    private RectInt32 CurrentScreen()
    {
        if (_placement is { } p)
        {
            // Look up the monitor under the saved one's center. (Enumerating DisplayArea.FindAll()
            // crashes the app with E_NOINTERFACE, so it is not used.)
            var center = new PointInt32(p.MonitorX + p.MonitorWidth / 2, p.MonitorY + p.MonitorHeight / 2);
            if (DisplayArea.GetFromPoint(center, DisplayAreaFallback.None) is { } area)
            {
                RectInt32 b = area.OuterBounds;
                if (b.X == p.MonitorX && b.Y == p.MonitorY && b.Width == p.MonitorWidth && b.Height == p.MonitorHeight)
                    return b;
            }
        }
        return DisplayArea.Primary.OuterBounds;
    }

    private static double ScaleOf(RectInt32 screen) =>
        Win32.GetMonitorScale(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);

    // ---- Moving along the top edge ----

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed || !Win32.TryGetCursorPosition(out int x, out _)) return;

        _isMoving = true;
        _grabOffsetX = x - (AppWindow.Position.X + AppWindow.Size.Width / 2);
        _content.MoveHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMoving || !Win32.TryGetCursorPosition(out int x, out int y)) return;

        // Whichever monitor the pointer is over; the island always hugs its top edge.
        RectInt32 screen = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest).OuterBounds;

        // Keep the open island fully on screen, so it stays centered over the resting line.
        int halfOpen = (int)Math.Round(OpenSize.Width * ScaleOf(screen) / 2);
        int center = Math.Clamp(x - _grabOffsetX, screen.X + halfOpen, screen.X + screen.Width - halfOpen);

        _placement = new IslandPlacement
        {
            MonitorX = screen.X,
            MonitorY = screen.Y,
            MonitorWidth = screen.Width,
            MonitorHeight = screen.Height,
            CenterFraction = (center - screen.X) / (double)screen.Width,
        };
        PlaceWindow(_windowSize);
    }

    private void Grip_PointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMoving) return;
        _isMoving = false;
        _content.MoveHandle.ReleasePointerCaptures();
        _settings.SetIsland(_placement);
    }

    /// <summary>Puts the island back at the center of the primary monitor.</summary>
    public void ResetPosition()
    {
        _placement = null;
        _settings.SetIsland(null);
        PlaceWindow(_windowSize);
    }

    private void OnDisplayChanged()
    {
        DispatcherQueue.TryEnqueue(() => PlaceWindow(_windowSize));
    }

    /// <summary>Hides the island while an app covers the screen, and brings it back afterwards.</summary>
    private void UpdateFullscreenState()
    {
        bool fullscreen = Win32.IsFullscreenAppActive();
        if (fullscreen == _isHiddenForFullscreen) return;
        _isHiddenForFullscreen = fullscreen;

        if (fullscreen)
        {
            _closeTimer.Stop();
            if (_isOpen) SetOpen(false);
            AppWindow.Hide();
        }
        else
        {
            PlaceWindow(_windowSize);
            AppWindow.Show(activateWindow: false);
        }
    }

    // ---- Compact content ----

    private void OnModulesChanged()
    {
        if (!UpdateCompactModule()) return;
        // While open, the compact content is hidden anyway; it shows again on the way back to rest.
        if (!_isOpen) GoToRest();
    }

    /// <summary>Picks the module whose compact content shows at rest. Returns whether it changed.</summary>
    private bool UpdateCompactModule()
    {
        IIslandModule? module = _registry.EnabledModules.FirstOrDefault(m => m.HasCompactActivity);
        if (module == _compactModule) return false;

        _compactModule = module;
        if (module is not null)
        {
            CompactLeading.Content = module.CreateCompactLeading();
            CompactTrailing.Content = module.CreateCompactTrailing();
        }
        // When there is nothing to show, the old content stays until its fade-out ends.
        return true;
    }

    // ---- Pointer and drag ----

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _closeTimer.Stop();
        SetOpen(true);
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _closeTimer.Start();
    }

    // Dragging files over the island opens it, so the files can be dropped on a module's page.
    // Pointer events do not fire during a drag, so the drag events stand in for them.
    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        if (!CanAcceptDrop(e)) return;
        _closeTimer.Stop();
        SetOpen(true);
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!CanAcceptDrop(e)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"Soltar en {FirstDropTarget()!.Title}";
    }

    private void Root_DragLeave(object sender, DragEventArgs e) => _closeTimer.Start();

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        IIslandModule? target = FirstDropTarget();
        if (target is null || !HasFiles(e.DataView)) return;

        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            // Items without a path (for example from a zip or a phone) cannot be kept as references.
            List<string> paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count == 0) return;

            target.ReceiveFiles(paths);
            _content.Select(target);
        }
        finally
        {
            deferral.Complete();
        }
    }

    // Some drag sources only advertise the classic "FileDrop" format; the storage items can still be read from it.
    private static bool HasFiles(DataPackageView data) =>
        data.Contains(StandardDataFormats.StorageItems) || data.AvailableFormats.Contains("FileDrop");

    private bool CanAcceptDrop(DragEventArgs e) => HasFiles(e.DataView) && FirstDropTarget() is not null;

    private IIslandModule? FirstDropTarget() => _registry.EnabledModules.FirstOrDefault(m => m.AcceptsFileDrops);

    // ---- Open and close ----

    private void SetOpen(bool open)
    {
        if (open == _isOpen) return;
        _isOpen = open;

        if (open)
        {
            // Grow the window first so the pill has room to expand into.
            PlaceWindow(OpenSize);
            Pill.CornerRadius = OpenCorners;
            Animate(OpenSize, OpenColor, offsetY: 0, contentOpacity: 1, compactOpacity: 0, onCompleted: null);
        }
        else
        {
            GoToRest();
        }
    }

    private void GoToRest()
    {
        RestState rest = Rest();

        // Make sure the window is big enough for the whole transition; shrink it afterwards.
        PlaceWindow(new Size(
            Math.Max(_windowSize.Width, rest.Window.Width),
            Math.Max(_windowSize.Height, rest.Window.Height)));

        Animate(rest.Pill, rest.Color, rest.OffsetY, contentOpacity: 0, rest.CompactOpacity, onCompleted: () =>
        {
            // Settle only if nothing reopened or changed the rest state meanwhile.
            if (_isOpen) return;
            RestState settled = Rest();
            Pill.CornerRadius = settled.Corners;
            PlaceWindow(settled.Window);
            if (_compactModule is null)
            {
                CompactLeading.Content = null;
                CompactTrailing.Content = null;
            }
        });
    }

    private void Animate(
        Size size, Color color, double offsetY, double contentOpacity, double compactOpacity, Action? onCompleted)
    {
        // Start from wherever a running animation left the pill, so reversing mid-way is smooth.
        double fromWidth = Pill.ActualWidth;
        double fromHeight = Pill.ActualHeight;
        Color fromColor = PillBrush.Color;
        double fromOffset = PillShift.Y;
        double fromContent = ContentHost.Opacity;
        double fromCompact = CompactHost.Opacity;
        _storyboard?.Stop();
        Pill.Width = fromWidth;
        Pill.Height = fromHeight;
        PillBrush.Color = fromColor;
        PillShift.Y = fromOffset;
        ContentHost.Opacity = fromContent;
        CompactHost.Opacity = fromCompact;

        if (contentOpacity > 0) ContentHost.Visibility = Visibility.Visible;
        if (compactOpacity > 0) CompactHost.Visibility = Visibility.Visible;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        storyboard.Children.Add(Make(new DoubleAnimation { From = fromWidth, To = size.Width }, Pill, "Width", easing));
        storyboard.Children.Add(Make(new DoubleAnimation { From = fromHeight, To = size.Height }, Pill, "Height", easing));
        storyboard.Children.Add(Make(new DoubleAnimation { From = fromOffset, To = offsetY }, PillShift, "Y", easing));
        storyboard.Children.Add(Make(new ColorAnimation { From = fromColor, To = color }, PillBrush, "Color", easing));
        storyboard.Children.Add(Fade(ContentHost, fromContent, contentOpacity, easing));
        storyboard.Children.Add(Fade(CompactHost, fromCompact, compactOpacity, easing));

        storyboard.Completed += (_, _) =>
        {
            if (_storyboard != storyboard) return;
            // Storyboards do not keep their end values once they finish; write them down.
            ContentHost.Opacity = contentOpacity;
            CompactHost.Opacity = compactOpacity;
            ContentHost.Visibility = contentOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            CompactHost.Visibility = compactOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            Pill.Width = size.Width;
            Pill.Height = size.Height;
            PillBrush.Color = color;
            PillShift.Y = offsetY;
            onCompleted?.Invoke();
        };

        _storyboard = storyboard;
        storyboard.Begin();
    }

    /// <summary>Fades in once the pill is mostly grown, and out right away before it shrinks.</summary>
    private static DoubleAnimation Fade(DependencyObject target, double from, double to, EasingFunctionBase easing)
    {
        DoubleAnimation fade = Make(new DoubleAnimation { From = from, To = to }, target, "Opacity", easing);
        fade.Duration = TimeSpan.FromMilliseconds(to > from ? 160 : 90);
        fade.BeginTime = to > from ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero;
        return fade;
    }

    private static T Make<T>(T animation, DependencyObject target, string property, EasingFunctionBase easing)
        where T : Timeline
    {
        animation.Duration = AnimationDuration;
        switch (animation)
        {
            case DoubleAnimation d:
                d.EasingFunction = easing;
                d.EnableDependentAnimation = true;
                break;
            case ColorAnimation c:
                c.EasingFunction = easing;
                c.EnableDependentAnimation = true;
                break;
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}

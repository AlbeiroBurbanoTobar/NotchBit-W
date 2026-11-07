using Microsoft.UI;
using NotchBit.Core;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;

namespace NotchBit.Island;

/// <summary>
/// The floating "island" at the top center of the primary screen.
/// At rest it is a thin white line, like the gesture bar on a phone. On hover the line grows into
/// the black island. While at rest the window is only slightly larger than the line, so it never
/// blocks clicks on whatever is around it.
/// </summary>
public sealed partial class IslandWindow : Window
{
    /// <summary>Hover target at rest: bigger than the line so it is easy to reach.</summary>
    private static readonly Size RestWindowSize = new(160, 14);
    private static readonly Size OpenSize = new(560, 200);

    private static readonly Size LineSize = new(72, 4);
    private const double LineOffsetY = 4;
    private static readonly Color LineColor = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
    private static readonly CornerRadius LineCorners = new(2);

    private static readonly Color OpenColor = Colors.Black;
    private static readonly CornerRadius OpenCorners = new(0, 0, 24, 24);

    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(260);
    /// <summary>Grace period after the pointer leaves, so brushing past the edge does not close it.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(250);

    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _closeTimer;
    private readonly DispatcherQueueTimer _fullscreenTimer;
    private readonly ModuleRegistry _registry;
    private readonly IslandContent _content;
    private Storyboard? _storyboard;
    private Size _windowSize = RestWindowSize;
    private bool _isOpen;
    private bool _isHiddenForFullscreen;

    public IslandWindow(ModuleRegistry registry)
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        _registry = registry;
        _content = new IslandContent(registry);
        ContentHost.Children.Add(_content);

        SystemBackdrop = new TransparentTintBackdrop();
        ConfigureChrome();

        ApplyRestState();
        PlaceWindow(RestWindowSize);

        _closeTimer = DispatcherQueue.CreateTimer();
        _closeTimer.Interval = CloseDelay;
        _closeTimer.IsRepeating = false;
        _closeTimer.Tick += (_, _) => SetOpen(false);

        // Monitors added or removed, resolution or scale changed: stay centered on the primary one.
        // Re-placing is deferred, since this fires inside the window procedure.
        Win32.DisplayChanged += OnDisplayChanged;

        // There is no notification for "an app went fullscreen", so check a few times a second.
        _fullscreenTimer = DispatcherQueue.CreateTimer();
        _fullscreenTimer.Interval = TimeSpan.FromMilliseconds(500);
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenState();
        _fullscreenTimer.Start();

        Closed += (_, _) =>
        {
            Win32.DisplayChanged -= OnDisplayChanged;
            _fullscreenTimer.Stop();
        };
    }

    public void ShowWithoutActivating() => AppWindow.Show(activateWindow: false);

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

    private void ApplyRestState()
    {
        Pill.Width = LineSize.Width;
        Pill.Height = LineSize.Height;
        Pill.CornerRadius = LineCorners;
        PillBrush.Color = LineColor;
        PillShift.Y = LineOffsetY;
    }

    /// <summary>Sizes the window to <paramref name="size"/> (in DIPs), centered at the top of the primary screen.</summary>
    private void PlaceWindow(Size size)
    {
        _windowSize = size;
        // Use the primary monitor's scale, not the window's: right after a monitor change the
        // window can still carry the old monitor's DPI.
        double scale = Win32.GetPrimaryMonitorScale();
        RectInt32 screen = DisplayArea.Primary.OuterBounds;

        int width = (int)Math.Round(size.Width * scale);
        int height = (int)Math.Round(size.Height * scale);
        int x = screen.X + (screen.Width - width) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, screen.Y, width, height));
    }

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

    private void SetOpen(bool open)
    {
        if (open == _isOpen) return;
        _isOpen = open;

        if (open)
        {
            // Grow the window first so the pill has room to expand into.
            PlaceWindow(OpenSize);
            Pill.CornerRadius = OpenCorners;
            ContentHost.Visibility = Visibility.Visible;
            Animate(OpenSize, OpenColor, offsetY: 0, contentOpacity: 1, onCompleted: null);
        }
        else
        {
            Animate(LineSize, LineColor, LineOffsetY, contentOpacity: 0, onCompleted: () =>
            {
                // Shrink the window only after the pill is a line again, unless it reopened meanwhile.
                if (_isOpen) return;
                ContentHost.Visibility = Visibility.Collapsed;
                Pill.CornerRadius = LineCorners;
                PlaceWindow(RestWindowSize);
            });
        }
    }

    private void Animate(Size size, Color color, double offsetY, double contentOpacity, Action? onCompleted)
    {
        // Start from wherever a running animation left the pill, so reversing mid-way is smooth.
        double fromWidth = Pill.ActualWidth;
        double fromHeight = Pill.ActualHeight;
        Color fromColor = PillBrush.Color;
        double fromOffset = PillShift.Y;
        double fromOpacity = ContentHost.Opacity;
        _storyboard?.Stop();
        Pill.Width = fromWidth;
        Pill.Height = fromHeight;
        PillBrush.Color = fromColor;
        PillShift.Y = fromOffset;
        ContentHost.Opacity = fromOpacity;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        storyboard.Children.Add(Make(new DoubleAnimation { From = fromWidth, To = size.Width }, Pill, "Width", easing));
        storyboard.Children.Add(Make(new DoubleAnimation { From = fromHeight, To = size.Height }, Pill, "Height", easing));
        storyboard.Children.Add(Make(new DoubleAnimation { From = fromOffset, To = offsetY }, PillShift, "Y", easing));
        storyboard.Children.Add(Make(new ColorAnimation { From = fromColor, To = color }, PillBrush, "Color", easing));

        // The page fades in once the pill is mostly open, and out before the pill shrinks.
        DoubleAnimation fade = Make(new DoubleAnimation { From = fromOpacity, To = contentOpacity }, ContentHost, "Opacity", easing);
        fade.Duration = TimeSpan.FromMilliseconds(contentOpacity > 0 ? 160 : 90);
        fade.BeginTime = contentOpacity > 0 ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero;
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) =>
        {
            if (_storyboard != storyboard) return;
            // Storyboards do not keep their end values once they finish; write them down.
            ContentHost.Opacity = contentOpacity;
            Pill.Width = size.Width;
            Pill.Height = size.Height;
            PillBrush.Color = color;
            PillShift.Y = offsetY;
            onCompleted?.Invoke();
        };

        _storyboard = storyboard;
        storyboard.Begin();
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

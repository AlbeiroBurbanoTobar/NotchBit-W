using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NotchBit.Core;

namespace NotchBit.Modules.Music;

/// <summary>What is playing, with controls; and a compact artwork-and-bars view while something plays.</summary>
public sealed class MusicModule : IIslandModule
{
    /// <summary>
    /// Players report "not playing" for a moment between tracks. Waiting before dropping the
    /// compact view keeps the island from collapsing and reopening on every song change.
    /// </summary>
    private static readonly TimeSpan StopGrace = TimeSpan.FromMilliseconds(1500);

    private readonly MusicController _controller;
    private readonly DispatcherQueueTimer _graceTimer;
    private MusicPlayerView? _view;
    private UIElement? _leading;
    private UIElement? _trailing;
    private bool _isActive;

    public MusicModule(DispatcherQueue ui)
    {
        _controller = new MusicController(ui);
        _graceTimer = ui.CreateTimer();
        _graceTimer.Interval = StopGrace;
        _graceTimer.IsRepeating = false;
        _graceTimer.Tick += (_, _) => UpdateActivity();

        _controller.Changed += OnControllerChanged;
        _ = _controller.StartAsync();
    }

    public string Id => "music";
    public string Title => "Música";
    public string Glyph => "";

    public bool HasCompactActivity => _isActive;
    public event Action? CompactActivityChanged;

    public UIElement CreateView() => _view ??= new MusicPlayerView(_controller);
    public UIElement? CreateCompactLeading() => _leading ??= MusicCompactViews.CreateArtwork(_controller);
    public UIElement? CreateCompactTrailing() => _trailing ??= MusicCompactViews.CreateBars(_controller);

    private void OnControllerChanged()
    {
        if (_controller.Current?.Info.IsPlaying == true)
        {
            _graceTimer.Stop();
            UpdateActivity();
        }
        else if (_isActive && !_graceTimer.IsRunning)
        {
            _graceTimer.Start();
        }
    }

    private void UpdateActivity()
    {
        bool playing = _controller.Current?.Info.IsPlaying == true;
        if (playing == _isActive) return;

        _isActive = playing;
        CompactActivityChanged?.Invoke();
    }
}

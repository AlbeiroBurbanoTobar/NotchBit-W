using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace NotchBit.Modules.Music;

/// <summary>The Música tab: artwork, track, a seekable progress bar and the playback buttons.</summary>
public sealed partial class MusicPlayerView : UserControl
{
    private readonly MusicController _controller;
    private readonly DispatcherQueueTimer _ticker;
    /// <summary>True while the code, not the user, moves the slider.</summary>
    private bool _isUpdating;
    private bool _isScrubbing;

    public MusicPlayerView(MusicController controller)
    {
        InitializeComponent();
        _controller = controller;

        // The position is estimated between readings, so the bar advances smoothly on its own.
        _ticker = DispatcherQueue.CreateTimer();
        _ticker.Interval = TimeSpan.FromMilliseconds(500);
        _ticker.Tick += (_, _) => UpdatePosition();

        // The slider marks its pointer events handled, so ask to hear about them anyway.
        Progress.AddHandler(PointerReleasedEvent, new PointerEventHandler(Scrub_Ended), handledEventsToo: true);
        Progress.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Scrub_Ended), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            _controller.Changed -= Refresh;
            _controller.Changed += Refresh;
            _ticker.Start();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _controller.Changed -= Refresh;
            _ticker.Stop();
        };
    }

    private void Refresh()
    {
        MusicSession? session = _controller.Current;
        EmptyText.Visibility = session is null ? Visibility.Visible : Visibility.Collapsed;
        Player.Visibility = session is null ? Visibility.Collapsed : Visibility.Visible;
        if (session is null) return;

        NowPlayingInfo info = session.Info;
        TitleText.Text = info.Title;
        ArtistText.Text = string.Join(" · ", new[] { info.Artist, info.Album }.Where(s => !string.IsNullOrWhiteSpace(s)));

        ArtBrush.ImageSource = session.Artwork;
        ArtPlaceholder.Visibility = session.Artwork is null ? Visibility.Visible : Visibility.Collapsed;

        PlayIcon.Glyph = info.IsPlaying ? "" : "";
        PlayButton.IsEnabled = info.CanTogglePlayPause;
        PreviousButton.IsEnabled = info.CanPrevious;
        NextButton.IsEnabled = info.CanNext;

        // A single YouTube video, for example, cannot skip, and live streams cannot seek.
        bool canSeek = info.CanSeek && info.Duration > TimeSpan.Zero;
        _isUpdating = true;
        Progress.IsEnabled = canSeek;
        Progress.Maximum = Math.Max(1, info.Duration.TotalSeconds);
        _isUpdating = false;
        DurationText.Text = info.Duration > TimeSpan.Zero ? Format(info.Duration) : "";

        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (_isScrubbing || _controller.Current is not { } session) return;

        TimeSpan position = session.Info.PositionAt(DateTimeOffset.UtcNow);
        _isUpdating = true;
        Progress.Value = Math.Min(position.TotalSeconds, Progress.Maximum);
        _isUpdating = false;
        ElapsedText.Text = Format(position);
    }

    private void Progress_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdating) return;
        _isScrubbing = true;
        ElapsedText.Text = Format(TimeSpan.FromSeconds(e.NewValue));
    }

    private void Scrub_Ended(object sender, PointerRoutedEventArgs e)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        _controller.Seek(TimeSpan.FromSeconds(Progress.Value));
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _controller.TogglePlayPause();
    private void Previous_Click(object sender, RoutedEventArgs e) => _controller.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => _controller.Next();

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}

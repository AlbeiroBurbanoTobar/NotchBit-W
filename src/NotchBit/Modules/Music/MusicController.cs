using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Windows.UI;

namespace NotchBit.Modules.Music;

/// <summary>What the island shows about the current track.</summary>
public sealed record MusicSession(NowPlayingInfo Info, BitmapImage? Artwork, Color? Accent);

/// <summary>
/// Tracks what is playing and forwards playback commands to that player.
/// Everything here runs on the UI thread; <see cref="Changed"/> is raised there too.
/// </summary>
public sealed class MusicController
{
    /// <summary>Players raise events on changes; this re-sync catches seeks and missed events.</summary>
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherQueue _ui;
    private readonly GsmtcProvider _provider = new();
    private readonly DispatcherQueueTimer _resync;

    private bool _isRefreshing;
    private bool _needsAnotherRefresh;
    private PendingChange? _pending;
    private string? _artworkKey;
    /// <summary>The track whose artwork is being loaded right now, so a load is not started twice.</summary>
    private string? _loadingArtworkKey;
    /// <summary>Tries so far for the current track; an image that cannot be decoded is not retried forever.</summary>
    private int _artworkAttempts;
    private const int MaxArtworkAttempts = 3;

    public MusicController(DispatcherQueue ui)
    {
        _ui = ui;
        _resync = ui.CreateTimer();
        _resync.Interval = ResyncInterval;
        _resync.Tick += (_, _) => RequestRefresh();
    }

    public MusicSession? Current { get; private set; }

    /// <summary>Raised whenever <see cref="Current"/> changes, and on every position update.</summary>
    public event Action? Changed;

    public async Task StartAsync()
    {
        // Windows raises events on its own threads; hop to the UI thread before touching anything.
        _provider.Changed += () => _ui.TryEnqueue(RequestRefresh);
        try
        {
            await _provider.StartAsync();
        }
        catch (COMException)
        {
            // No media session service (very rare); the island just never shows music.
            return;
        }
        _resync.Start();
    }

    public void TogglePlayPause()
    {
        if (Current is not { } session || !session.Info.CanTogglePlayPause) return;
        Send(s => s.TryTogglePlayPauseAsync(), isPlaying: !session.Info.IsPlaying, position: null);
    }

    public void Next()
    {
        if (Current is { Info.CanNext: true }) Send(s => s.TrySkipNextAsync(), isPlaying: null, position: null);
    }

    public void Previous()
    {
        if (Current is { Info.CanPrevious: true }) Send(s => s.TrySkipPreviousAsync(), isPlaying: null, position: null);
    }

    public void Seek(TimeSpan position)
    {
        if (Current is not { } session || !session.Info.CanSeek) return;
        Send(s => s.TryChangePlaybackPositionAsync((session.Info.Start + position).Ticks), isPlaying: null, position: position);
    }

    private void Send(
        Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command,
        bool? isPlaying, TimeSpan? position)
    {
        GlobalSystemMediaTransportControlsSession? target = _provider.ActiveSession;
        if (target is null || Current is not { } session) return;

        // Show the result right away; the player takes a moment to apply the command.
        var pending = new PendingChange(session.Info.AppId, isPlaying, position, DateTimeOffset.UtcNow);
        _pending = pending;
        Current = session with { Info = pending.Apply(session.Info) };
        Changed?.Invoke();

        _ = RunAsync(command, target);
    }

    private async Task RunAsync(
        Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command,
        GlobalSystemMediaTransportControlsSession target)
    {
        try
        {
            await command(target);
        }
        catch (COMException)
        {
            // The player went away mid-command; the next refresh drops it.
        }
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        if (_isRefreshing)
        {
            _needsAnotherRefresh = true;
            return;
        }
        _ = RefreshLoopAsync();
    }

    /// <summary>Reads once, and again if more changes arrived meanwhile, so none are lost.</summary>
    private async Task RefreshLoopAsync()
    {
        _isRefreshing = true;
        try
        {
            do
            {
                _needsAnotherRefresh = false;
                await RefreshOnceAsync();
            }
            while (_needsAnotherRefresh);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshOnceAsync()
    {
        NowPlayingSnapshot? snapshot = await _provider.ReadAsync();
        if (snapshot is null)
        {
            _pending = null;
            _artworkKey = null;
            if (Current is not null)
            {
                Current = null;
                Changed?.Invoke();
            }
            return;
        }

        NowPlayingInfo info = snapshot.Info;
        if (_pending is { } pending)
        {
            // A reading taken just after a command can still show the old state; showing it
            // would make the button flicker back. Trust the player once it agrees or time runs out.
            if (pending.IsSettledBy(info)) _pending = null;
            else info = pending.Apply(info);
        }

        // Keep the artwork while the same track plays.
        MusicSession? previous = Current;
        bool sameTrack = previous is not null && info.TrackKey == _artworkKey;
        if (!sameTrack)
        {
            _artworkKey = info.TrackKey;
            _artworkAttempts = 0;
        }

        Current = new MusicSession(info, sameTrack ? previous!.Artwork : null, sameTrack ? previous!.Accent : null);
        Changed?.Invoke();

        // Load the artwork whenever the track has none yet and Windows offers one. Browsers send
        // the title first and the thumbnail a moment later, for the same track, so waiting for the
        // track to change would miss it.
        if (Current.Artwork is null
            && snapshot.Thumbnail is not null
            && _loadingArtworkKey != info.TrackKey
            && _artworkAttempts < MaxArtworkAttempts)
        {
            _artworkAttempts++;
            _ = LoadArtworkAsync(snapshot.Thumbnail, info.TrackKey);
        }
    }

    private async Task LoadArtworkAsync(IRandomAccessStreamReference thumbnail, string key)
    {
        _loadingArtworkKey = key;
        try
        {
            byte[] bytes = await ReadAllAsync(thumbnail);
            if (bytes.Length == 0)
            {
                // An empty placeholder, sent before the real image is ready: not a failed try.
                if (_artworkKey == key) _artworkAttempts--;
                return;
            }

            // A fresh stream for each consumer, since each one reads it to the end.
            var image = new BitmapImage();
            await image.SetSourceAsync(await ToStreamAsync(bytes));
            Color? accent = await ArtworkPalette.DominantColorAsync(await ToStreamAsync(bytes));

            // The track may have changed while this was loading.
            if (_artworkKey != key || Current is not { } session) return;
            Current = session with { Artwork = image, Accent = accent };
            Changed?.Invoke();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException or IOException)
        {
            // No usable artwork; the island shows a music note instead.
        }
        finally
        {
            if (_loadingArtworkKey == key) _loadingArtworkKey = null;
        }
    }

    private static async Task<byte[]> ReadAllAsync(IRandomAccessStreamReference reference)
    {
        using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<InMemoryRandomAccessStream> ToStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        return stream;
    }

    /// <summary>
    /// A command's expected effect, kept until the player reports it. Without this, the play button
    /// would flip, flip back on a stale reading, and flip again.
    /// </summary>
    private sealed record PendingChange(string AppId, bool? IsPlaying, TimeSpan? Position, DateTimeOffset IssuedAt)
    {
        /// <summary>How long readings that contradict the command are ignored.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
        /// <summary>Readings come a little after a seek, so a small drift still counts as confirmed.</summary>
        private static readonly TimeSpan PositionTolerance = TimeSpan.FromSeconds(2);

        public bool IsSettledBy(NowPlayingInfo info)
        {
            if (info.AppId != AppId || DateTimeOffset.UtcNow - IssuedAt > Timeout) return true;

            bool playStateMatches = IsPlaying is null || IsPlaying == info.IsPlaying;
            bool positionMatches = Position is null
                || (info.PositionAt(DateTimeOffset.UtcNow) - Position.Value).Duration() < PositionTolerance;
            return playStateMatches && positionMatches;
        }

        public NowPlayingInfo Apply(NowPlayingInfo info)
        {
            if (info.AppId != AppId) return info;

            NowPlayingInfo result = info;
            if (IsPlaying is { } playing)
            {
                // Freeze the position at the moment of the command, then let it run (or not).
                result = result with
                {
                    IsPlaying = playing,
                    Position = info.PositionAt(IssuedAt),
                    PositionTime = IssuedAt,
                };
            }
            if (Position is { } position)
            {
                result = result with { Position = position, PositionTime = IssuedAt };
            }
            return result;
        }
    }
}

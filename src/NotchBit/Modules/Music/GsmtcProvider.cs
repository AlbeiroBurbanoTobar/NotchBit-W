using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NotchBit.Modules.Music;

/// <summary>A reading together with the session it came from and the track's artwork, if any.</summary>
public sealed record NowPlayingSnapshot(
    NowPlayingInfo Info,
    IRandomAccessStreamReference? Thumbnail,
    GlobalSystemMediaTransportControlsSession Session);

/// <summary>
/// Reads Now Playing from Windows (the System Media Transport Controls), which covers Spotify,
/// browsers, Apple Music and anything else that shows up in the volume flyout.
/// </summary>
public sealed class GsmtcProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionHooks> _hooks = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    /// <summary>The player that most recently reported a change wins when several are playing.</summary>
    private string? _preferredId;

    /// <summary>Raised on a background thread whenever any player reports a change.</summary>
    public event Action? Changed;

    /// <summary>The session the last reading came from; commands go there.</summary>
    public GlobalSystemMediaTransportControlsSession? ActiveSession { get; private set; }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => { SyncHooks(); Changed?.Invoke(); };
        _manager.CurrentSessionChanged += (_, _) => Changed?.Invoke();
        SyncHooks();
        Changed?.Invoke();
    }

    public async Task<NowPlayingSnapshot?> ReadAsync()
    {
        GlobalSystemMediaTransportControlsSession? session = PickSession();
        ActiveSession = session;
        if (session is null) return null;

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            // Players that quit leave an empty session behind for a moment.
            if (properties is null || string.IsNullOrWhiteSpace(properties.Title)) return null;

            // Some apps never fill in the update time; Windows then reports year 1.
            DateTimeOffset positionTime = timeline.LastUpdatedTime.Year < 2000
                ? DateTimeOffset.UtcNow
                : timeline.LastUpdatedTime;

            GlobalSystemMediaTransportControlsSessionPlaybackControls controls = playback.Controls;
            var info = new NowPlayingInfo(
                AppId: session.SourceAppUserModelId,
                Title: properties.Title,
                Artist: properties.Artist,
                Album: properties.AlbumTitle,
                IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Start: timeline.StartTime,
                Position: timeline.Position - timeline.StartTime,
                PositionTime: positionTime,
                Duration: timeline.EndTime - timeline.StartTime,
                CanTogglePlayPause: controls.IsPlayEnabled || controls.IsPauseEnabled,
                CanNext: controls.IsNextEnabled,
                CanPrevious: controls.IsPreviousEnabled,
                CanSeek: controls.IsPlaybackPositionEnabled);

            return new NowPlayingSnapshot(info, properties.Thumbnail, session);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The player closed between listing it and reading it.
            return null;
        }
    }

    private GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_manager is null) return null;

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = _manager.GetSessions();
        GlobalSystemMediaTransportControlsSession? preferred =
            sessions.FirstOrDefault(s => s.SourceAppUserModelId == _preferredId);

        if (preferred is not null && IsPlaying(preferred)) return preferred;
        return sessions.FirstOrDefault(IsPlaying) ?? preferred ?? _manager.GetCurrentSession();
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>Starts listening to new players and stops listening to the ones that are gone.</summary>
    private void SyncHooks()
    {
        if (_manager is null) return;
        var current = _manager.GetSessions().ToDictionary(s => s.SourceAppUserModelId);

        lock (_gate)
        {
            foreach (string id in _hooks.Keys.Where(id => !current.ContainsKey(id)).ToList())
            {
                _hooks[id].Dispose();
                _hooks.Remove(id);
            }
            foreach ((string id, GlobalSystemMediaTransportControlsSession session) in current)
            {
                if (!_hooks.ContainsKey(id)) _hooks[id] = new SessionHooks(session, OnSessionEvent);
            }
        }
    }

    private void OnSessionEvent(string appId)
    {
        _preferredId = appId;
        Changed?.Invoke();
    }

    private sealed class SessionHooks : IDisposable
    {
        private readonly GlobalSystemMediaTransportControlsSession _session;
        private readonly Action<string> _onEvent;

        public SessionHooks(GlobalSystemMediaTransportControlsSession session, Action<string> onEvent)
        {
            _session = session;
            _onEvent = onEvent;
            session.MediaPropertiesChanged += OnProperties;
            session.PlaybackInfoChanged += OnPlayback;
            session.TimelinePropertiesChanged += OnTimeline;
        }

        private void OnProperties(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) => _onEvent(s.SourceAppUserModelId);
        private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) => _onEvent(s.SourceAppUserModelId);
        private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) => _onEvent(s.SourceAppUserModelId);

        public void Dispose()
        {
            _session.MediaPropertiesChanged -= OnProperties;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
    }
}

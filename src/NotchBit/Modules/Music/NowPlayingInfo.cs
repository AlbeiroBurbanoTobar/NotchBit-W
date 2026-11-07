namespace NotchBit.Modules.Music;

/// <summary>One reading of what a media app reports as playing.</summary>
/// <param name="Position">Where the track was at <paramref name="PositionTime"/>, not necessarily now.</param>
public sealed record NowPlayingInfo(
    string AppId,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    TimeSpan Start,
    TimeSpan Position,
    DateTimeOffset PositionTime,
    TimeSpan Duration,
    bool CanTogglePlayPause,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek)
{
    /// <summary>Changes when the track changes, so artwork is only reloaded then.</summary>
    public string TrackKey => $"{AppId}|{Title}|{Artist}|{Album}";

    /// <summary>
    /// Where the track is at <paramref name="now"/>. Apps report their position rarely
    /// (browsers especially), so while playing it advances from the reported time.
    /// </summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        TimeSpan position = IsPlaying ? Position + (now - PositionTime) : Position;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration > TimeSpan.Zero && position > Duration) position = Duration;
        return position;
    }
}

using Radha.Core.Services;
using Windows.Media.Control;

namespace Radha.App.Services.Win;

/// <summary>Windows equivalent of MPRIS: GlobalSystemMediaTransportControls (SMTC).</summary>
public sealed class GsmtcMediaService : IMediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    public MediaTrack Current { get; private set; } = MediaTrack.None;
    public PlaybackState State { get; private set; } = PlaybackState.Closed;
    public TimeSpan Position => _session?.GetTimelineProperties()?.Position ?? TimeSpan.Zero;

    public event EventHandler? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Attach(_manager.GetCurrentSession());
            Attach(_manager.GetCurrentSession());
        }
        catch
        {
            // SMTC can be unavailable on server SKUs / locked-down policies.
            State = PlaybackState.Closed;
        }
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;

        if (_session is null)
        {
            Current = MediaTrack.None;
            State = PlaybackState.Closed;
            Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _ = RefreshAsync();
        UpdatePlaybackState();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, object args) => UpdatePlaybackState();

    private async Task RefreshAsync()
    {
        if (_session is null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            Current = new MediaTrack(
                props?.Title ?? "",
                props?.Artist ?? "",
                props?.AlbumTitle ?? "",
                _session.GetTimelineProperties()?.EndTime ?? TimeSpan.Zero,
                _session.SourceAppUserModelId ?? "");
        }
        catch { Current = MediaTrack.None; }

        UpdatePlaybackState();
        Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private void UpdatePlaybackState()
    {
        State = _session?.GetPlaybackInfo()?.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
            _ => PlaybackState.Closed
        };
        Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public async Task PlayPauseAsync()
    {
        if (_session is not null) await _session.TryTogglePlayPauseAsync();
    }

    public async Task NextAsync()
    {
        if (_session is not null) await _session.TrySkipNextAsync();
    }

    public async Task PreviousAsync()
    {
        if (_session is not null) await _session.TrySkipPreviousAsync();
    }

    public async Task SeekAsync(TimeSpan position)
    {
        if (_session is not null) await _session.TryChangePlaybackPositionAsync(position.Ticks);
    }
}

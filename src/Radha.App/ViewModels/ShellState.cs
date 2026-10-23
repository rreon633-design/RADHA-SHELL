using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Radha.Core.Security;
using Radha.Core.Services;
using Radha.Core.Theming;

namespace Radha.App.ViewModels;

/// <summary>Shared, observable UI state for every shell surface (bar, overlay, OSD).</summary>
public sealed partial class ShellState : ObservableObject
{
    private readonly IMediaService _media;
    private readonly INotificationService _notifications;
    private readonly IWorkspaceService _workspaces;
    private readonly IAudioService _audio;

    [ObservableProperty] private ThemeViewModel _theme;
    [ObservableProperty] private string _clockText = "--:--";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _activeWindowTitle = "Desktop";
    [ObservableProperty] private bool _isDashboardOpen;
    [ObservableProperty] private bool _isLauncherOpen;
    [ObservableProperty] private bool _isControlCenterOpen;
    [ObservableProperty] private bool _isNotificationsOpen;
    [ObservableProperty] private bool _isSessionOpen;
    [ObservableProperty] private bool _isDnd;
    [ObservableProperty] private bool _osdVisible;
    [ObservableProperty] private string _osdLabel = "Volume";
    [ObservableProperty] private double _osdValue;
    [ObservableProperty] private int _batteryPercent = 86;
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private string _mediaTitle = "Nothing playing";
    [ObservableProperty] private string _mediaArtist = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _hasNotifications;
    [ObservableProperty] private string _userName = Environment.UserName;
    [ObservableProperty] private string _uptimeText = "";

    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    public ObservableCollection<NotificationItemVm> Notifications { get; } = new();

    public event EventHandler? OverlayChanged;
    public Action? ScheduleOsdHide { get; set; }

    public bool IsOverlayOpen =>
        IsDashboardOpen || IsLauncherOpen || IsControlCenterOpen || IsNotificationsOpen || IsSessionOpen;

    public ShellState(
        ThemeViewModel theme,
        IMediaService media,
        INotificationService notifications,
        IWorkspaceService workspaces,
        IAudioService audio)
    {
        _theme = theme;
        _media = media;
        _notifications = notifications;
        _workspaces = workspaces;
        _audio = audio;

        _media.Changed += (_, _) => SyncMedia();
        _notifications.Received += (_, n) => PushNotification(n);
        _notifications.Cleared += (_, _) =>
        {
            Notifications.Clear();
            HasNotifications = false;
        };
        _workspaces.Changed += (_, _) => SyncWorkspaces();

        SyncMedia();
        SyncWorkspaces();
        IsDnd = notifications.DoNotDisturb;
        Tick();
    }

    // ---------------- commands ----------------

    [RelayCommand] private void ToggleDashboard() => TogglePanel(() => IsDashboardOpen, v => IsDashboardOpen = v);
    [RelayCommand] private void ToggleLauncher() => TogglePanel(() => IsLauncherOpen, v => IsLauncherOpen = v);
    [RelayCommand] private void ToggleControlCenter() => TogglePanel(() => IsControlCenterOpen, v => IsControlCenterOpen = v);
    [RelayCommand] private void ToggleNotifications() => TogglePanel(() => IsNotificationsOpen, v => IsNotificationsOpen = v);
    [RelayCommand] private void ToggleSession() => TogglePanel(() => IsSessionOpen, v => IsSessionOpen = v);

    [RelayCommand]
    public void CloseAll()
    {
        ClosePanels();
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleDnd()
    {
        IsDnd = !IsDnd;
        _notifications.DoNotDisturb = IsDnd;
    }

    [RelayCommand] private void ClearNotifications() => _notifications.ClearAll();
    [RelayCommand] private void DismissNotification(string id) => _notifications.Dismiss(id);
    [RelayCommand] private async Task PlayPauseAsync() => await _media.PlayPauseAsync();
    [RelayCommand] private async Task NextTrackAsync() => await _media.NextAsync();
    [RelayCommand] private async Task PreviousTrackAsync() => await _media.PreviousAsync();
    [RelayCommand] private void SwitchWorkspace(WorkspaceItem workspace) => _ = _workspaces.SwitchToAsync(workspace.Index);

    // ---------------- IPC-facing surface ----------------

    public void ShowModule(string module, string command, string[] args)
    {
        var open = command is "open" or "show" or "menu";
        var toggle = command == "toggle";

        switch (module.Replace("-", "").ToLowerInvariant())
        {
            case "bar":
                return; // always visible in v1; show/hide arrives with app-bar docking

            case "dashboard":
                SetOnly(() => IsDashboardOpen, v => IsDashboardOpen = v, toggle, open);
                break;

            case "launcher":
                SetOnly(() => IsLauncherOpen, v => IsLauncherOpen = v, toggle, open);
                break;

            case "controlcenter":
                SetOnly(() => IsControlCenterOpen, v => IsControlCenterOpen = v, toggle, open);
                break;

            case "notifications":
                if (command == "clear")
                {
                    _notifications.ClearAll();
                    return;
                }
                if (command.StartsWith("dnd", StringComparison.Ordinal))
                {
                    IsDnd = command == "dnd-on" || (command == "dnd-toggle" && !IsDnd);
                    _notifications.DoNotDisturb = IsDnd;
                    return;
                }
                SetOnly(() => IsNotificationsOpen, v => IsNotificationsOpen = v, toggle, open);
                break;

            case "session":
                SetOnly(() => IsSessionOpen, v => IsSessionOpen = v, toggle, open);
                break;

            case "osd":
                var label = command is "brightness" ? "Brightness" : command is "mic" ? "Microphone" : "Volume";
                var value = args.Length > 0 && InputGuard.TryParseNumber(args[0], 0, 100, out var parsed)
                    ? parsed
                    : command == "mic" ? (_audio.MicMuted ? 0 : 100) : _audio.Volume;
                ShowOsd(label, value);
                break;
        }
    }

    public void ApplyTheme(MaterialScheme scheme) => Theme = new ThemeViewModel(scheme);

    public void ShowOsd(string label, double value)
    {
        OsdLabel = label;
        OsdValue = Math.Clamp(value, 0, 100);
        OsdVisible = true;
        ScheduleOsdHide?.Invoke();
    }

    public void SetBattery(int percent, bool charging)
    {
        BatteryPercent = percent;
        IsCharging = charging;
    }

    public void Tick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm");
        DateText = now.ToString("ddd, d MMM");
        UptimeText = FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    // ---------------- helpers ----------------

    private void SyncMedia()
    {
        var track = _media.Current;
        MediaTitle = track.IsEmpty ? "Nothing playing" : track.Title;
        MediaArtist = track.IsEmpty ? "" : track.Artist;
        IsPlaying = _media.State == PlaybackState.Playing;
    }

    private void SyncWorkspaces()
    {
        Workspaces.Clear();
        foreach (var w in _workspaces.Workspaces)
            Workspaces.Add(new WorkspaceItem(w.Index, w.Active, w.Occupied, w.WindowCount));
    }

    /// <summary>Notifications are wrapped so list templates can carry their own theme brushes.</summary>
    partial void OnThemeChanged(ThemeViewModel value)
    {
        foreach (var item in Notifications) item.ApplyTheme(value);
    }

    private void PushNotification(ShellNotification notification)
    {
        Notifications.Insert(0, new NotificationItemVm(notification, Theme));
        HasNotifications = true;
    }

    private void ClosePanels() =>
        IsDashboardOpen = IsLauncherOpen = IsControlCenterOpen = IsNotificationsOpen = IsSessionOpen = false;

    private void TogglePanel(Func<bool> isOpen, Action<bool> set)
    {
        var opening = !isOpen();
        ClosePanels();
        if (opening) set(true);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetOnly(Func<bool> isOpen, Action<bool> set, bool toggle, bool open)
    {
        var target = toggle ? !isOpen() : open;
        ClosePanels();
        if (target) set(true);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatUptime(TimeSpan span) =>
        $"{(int)span.TotalDays}d {span.Hours:00}h {span.Minutes:00}m";
}

/// <summary>One notification row with pre-resolved brushes (DataTemplates cannot see the shell theme).</summary>
public sealed partial class NotificationItemVm : ObservableObject
{
    [ObservableProperty] private SolidColorBrush _titleBrush = new(Microsoft.UI.Colors.White);
    [ObservableProperty] private SolidColorBrush _bodyBrush = new(Microsoft.UI.Colors.Gray);
    [ObservableProperty] private SolidColorBrush _cardBrush = new(Microsoft.UI.Colors.Transparent);
    [ObservableProperty] private SolidColorBrush _accentBrush = new(Microsoft.UI.Colors.MediumPurple);
    [ObservableProperty] private SolidColorBrush _appNameBrush = new(Microsoft.UI.Colors.Gray);

    public NotificationItemVm(ShellNotification notification, ThemeViewModel theme)
    {
        Notification = notification;
        ApplyTheme(theme);
    }

    public ShellNotification Notification { get; }
    public string Id => Notification.Id;
    public string AppName => Notification.AppName;
    public string Title => Notification.Title;
    public string Body => Notification.Body;
    public string TimeText => Notification.ReceivedAt.ToLocalTime().ToString("HH:mm");

    public void ApplyTheme(ThemeViewModel theme)
    {
        TitleBrush = theme.OnSurface;
        BodyBrush = theme.OnSurfaceVariant;
        CardBrush = theme.SurfaceContainerHigh;
        AppNameBrush = theme.OnSurfaceVariant;
        AccentBrush = Notification.Urgency == NotificationUrgency.High ? theme.Error : theme.Primary;
    }
}

/// <summary>Workspace pill model (virtual desktops once the COM bridge lands).</summary>
public sealed partial class WorkspaceItem : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private bool _active;
    [ObservableProperty] private bool _occupied;
    [ObservableProperty] private int _windowCount;

    public WorkspaceItem(int index, bool active, bool occupied, int windowCount = 0)
    {
        _index = index;
        _active = active;
        _occupied = occupied;
        _windowCount = windowCount;
    }

    public string Label => (Index + 1).ToString();
}

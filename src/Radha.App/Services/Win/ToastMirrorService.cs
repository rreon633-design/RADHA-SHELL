using Radha.Core.Services;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Radha.App.Services.Win;

/// <summary>
/// Mirrors Windows toasts into the shell's notification centre.
///
/// <c>UserNotificationListener</c> is the only supported way to read toasts raised by other apps, and
/// it has two hard requirements:
///
///   1. **package identity** — it returns nothing useful from an unpackaged process, and
///   2. the restricted **userNotificationListener** capability, declared in
///      <c>Package.appxmanifest</c>.
///
/// Both are satisfied by the packaged build (`-p:RadhaPackaged=true`). When the app runs unpackaged,
/// <see cref="StartAsync"/> reports <c>false</c> and the notification centre keeps working for shell
/// events and IPC pushes — nothing is faked, and <see cref="UnavailableReason"/> says why.
///
/// The toast → <see cref="ShellNotification"/> mapping is <see cref="ToastMirror"/> in Radha.Core,
/// which is unit-tested on Linux.
/// </summary>
public sealed class ToastMirrorService : IDisposable
{
    private readonly INotificationService _sink;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private UserNotificationListener? _listener;
    private bool _started;

    public bool IsMirroring { get; private set; }
    public string? UnavailableReason { get; private set; }
    public int MirroredCount { get; private set; }

    public ToastMirrorService(INotificationService sink) => _sink = sink;

    /// <summary>Requests access once; safe to call repeatedly.</summary>
    public async Task<bool> StartAsync()
    {
        if (_started) return IsMirroring;
        _started = true;

        try
        {
            var listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                UnavailableReason = access switch
                {
                    UserNotificationListenerAccessStatus.Denied =>
                        "notification access denied — allow RADHA SHELL in Settings → Privacy → Notifications",
                    UserNotificationListenerAccessStatus.Unspecified =>
                        "notification access not granted (packaged build with the userNotificationListener capability required)",
                    _ => "notification listener unavailable",
                };
                return false;
            }

            _listener = listener;
            _listener.NotificationChanged += OnNotificationChanged;
            IsMirroring = true;
            UnavailableReason = null;

            await PollAsync();
            return true;
        }
        catch (Exception ex)
        {
            // Typically 0x80070490 (Element not found) from an unpackaged process: WinRT cannot
            // resolve the listener without package identity.
            Available = false;
            UnavailableReason = $"notification listener requires package identity ({ex.GetType().Name})";
            return false;
        }
    }

    /// <summary>True when the listener at least constructed; drives the diagnostics panel.</summary>
    public bool Available { get; private set; } = true;

    private async void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        try { await PollAsync(); } catch { /* listener torn down */ }
    }

    /// <summary>Pulls the current toast list and pushes anything new into the notification store.</summary>
    public async Task PollAsync()
    {
        var listener = _listener;
        if (listener is null) return;

        IReadOnlyList<UserNotification> toasts;
        try { toasts = await listener.GetNotificationsAsync(NotificationKinds.Toast); }
        catch { return; }

        foreach (var toast in toasts)
        {
            try
            {
                var binding = toast.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                IReadOnlyList<string> texts = binding is null
                    ? Array.Empty<string>()
                    : binding.GetTextElements().Select(t => t.Text).ToList();
                var title = texts.Count > 0 ? texts[0] : "";
                var body = texts.Count > 1 ? texts[1] : "";

                var mirrored = new MirroredToast(
                    AppId: toast.AppInfo?.AppUserModelId ?? "windows",
                    AppName: toast.AppInfo?.DisplayInfo?.DisplayName ?? "Windows",
                    Title: title,
                    Body: body,
                    ReceivedAt: toast.CreationTime);

                var shell = ToastMirror.ToShell(mirrored);
                if (!_seen.Add(shell.Id)) continue; // same toast seen on an earlier poll

                _sink.Push(shell);
                MirroredCount++;
            }
            catch { /* malformed toast from a third-party app */ }
        }
    }

    public void Dispose()
    {
        if (_listener is not null) _listener.NotificationChanged -= OnNotificationChanged;
        _listener = null;
        IsMirroring = false;
    }
}

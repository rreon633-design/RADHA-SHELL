using Radha.Core.Services;

namespace Radha.App.Services.Win;

/// <summary>
/// In-memory notification history feeding the bar badge and notification centre.
///
/// Phase-2: mirror system toasts through UserNotificationListener (requires MSIX identity and the
/// userNotificationListener capability). The listener is intentionally not faked here: when it is
/// added, it will call <see cref="Push"/> exactly like the CLI/IPC path does today.
/// </summary>
public sealed class NotificationStore : INotificationService
{
    private readonly List<ShellNotification> _history = new();
    private const int MaxHistory = 100;

    public bool DoNotDisturb { get; set; }
    public IReadOnlyList<ShellNotification> History => _history;
    public event EventHandler<ShellNotification>? Received;
    public event EventHandler? Cleared;

    public void Push(ShellNotification notification)
    {
        _history.Insert(0, notification);
        if (_history.Count > MaxHistory) _history.RemoveAt(_history.Count - 1);
        if (DoNotDisturb) return; // silence toasts but keep history
        Ui.Post(() => Received?.Invoke(this, notification));
    }

    public void Dismiss(string id)
    {
        _history.RemoveAll(n => n.Id == id);
        Ui.Post(() => Cleared?.Invoke(this, EventArgs.Empty));
    }

    public void ClearAll()
    {
        _history.Clear();
        Ui.Post(() => Cleared?.Invoke(this, EventArgs.Empty));
    }
}

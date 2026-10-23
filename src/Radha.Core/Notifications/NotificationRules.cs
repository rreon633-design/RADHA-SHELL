using Radha.Core.Services;

namespace Radha.Core.Notifications;

/// <summary>One app's worth of notifications, newest first.</summary>
public sealed record NotificationGroup(
    string AppId,
    string AppName,
    IReadOnlyList<ShellNotification> Items,
    int Total,
    bool HasActions);

public sealed record NotificationPolicy(
    bool DoNotDisturb = false,
    TimeSpan? LowExpiry = null,
    TimeSpan? NormalExpiry = null,
    TimeSpan? HighExpiry = null,
    int MaxPerGroup = 4,
    int MaxVisible = 12,
    TimeSpan? ClearAfter = null)
{
    public static NotificationPolicy Default { get; } = new(
        LowExpiry: TimeSpan.FromSeconds(20),
        NormalExpiry: TimeSpan.FromSeconds(8),
        HighExpiry: null,                       // high urgency never auto-expires
        ClearAfter: TimeSpan.FromDays(3));
}

/// <summary>
/// Grouping, expiry and Do-Not-Disturb rules for the notification centre.
///
/// Expiry here is about *toast* behaviour; the store keeps the item until
/// <see cref="NotificationPolicy.ClearAfter"/>. Keeping both rules in one pure type means the
/// window never has to know which of the two it is applying.
/// </summary>
public static class NotificationRules
{
    public static bool ShouldExpire(ShellNotification notification, NotificationPolicy policy, DateTimeOffset now)
    {
        if (policy.DoNotDisturb) return false;
        if (notification.Urgency == NotificationUrgency.High) return false;
        if (notification.Actions.Length > 0) return false;   // waiting on a decision

        var window = notification.Urgency switch
        {
            NotificationUrgency.Low => policy.LowExpiry,
            NotificationUrgency.Normal => policy.NormalExpiry,
            _ => policy.HighExpiry
        };

        if (window is null) return false;
        return now - notification.ReceivedAt >= window.Value;
    }

    /// <summary>True when a stored notification is old enough to be swept from the store.</summary>
    public static bool ShouldClear(ShellNotification notification, NotificationPolicy policy, DateTimeOffset now) =>
        policy.ClearAfter is { } window && now - notification.ReceivedAt >= window;

    public static bool ShouldToast(ShellNotification notification, NotificationPolicy policy) =>
        !policy.DoNotDisturb || notification.Urgency == NotificationUrgency.High;

    /// <summary>
    /// Groups by app, keeps every group ordered newest-first, caps each group at
    /// <see cref="NotificationPolicy.MaxPerGroup"/> and the whole view at
    /// <see cref="NotificationPolicy.MaxVisible"/>. Returns how many were hidden so the UI can say so.
    /// </summary>
    public static (IReadOnlyList<NotificationGroup> Groups, int Hidden) Group(
        IEnumerable<ShellNotification> items,
        NotificationPolicy policy,
        DateTimeOffset? now = null)
    {
        var ordered = items
            .OrderByDescending(n => n.ReceivedAt)
            .ToList();

        var groups = new List<NotificationGroup>();
        var hidden = 0;
        var visible = 0;

        foreach (var group in ordered.GroupBy(n => n.AppId, StringComparer.OrdinalIgnoreCase))
        {
            var all = group.OrderByDescending(n => n.ReceivedAt).ToList();
            var take = Math.Min(all.Count, Math.Max(1, policy.MaxPerGroup));
            var room = Math.Max(0, policy.MaxVisible - visible);
            if (room == 0)
            {
                hidden += all.Count;
                continue;
            }

            take = Math.Min(take, room);
            var shown = all.Take(take).ToList();
            hidden += all.Count - shown.Count;
            visible += shown.Count;

            groups.Add(new NotificationGroup(
                group.Key,
                shown[0].AppName,
                shown,
                all.Count,
                all.Any(n => n.Actions.Length > 0)));
        }

        return (groups, hidden);
    }

    /// <summary>Human label for the action row: "Reply", "Open", or the raw argument if unknown.</summary>
    public static string ActionLabel(string action) => action switch
    {
        "" => "Open",
        "open" => "Open",
        "reply" => "Reply",
        "dismiss" => "Dismiss",
        "snooze" => "Snooze",
        "mark-read" => "Mark read",
        _ => action
    };
}

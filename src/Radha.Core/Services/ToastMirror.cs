namespace Radha.Core.Services;

/// <summary>
/// Pure mapping from a mirrored Windows toast to the shell's notification model.
///
/// The listener itself (UserNotificationListener) is Windows-only and needs package identity; this
/// type only decides what a captured toast becomes, so the rules are testable anywhere.
/// </summary>
public readonly record struct MirroredToast(
    string AppId,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset ReceivedAt);

public static class ToastMirror
{
    /// <summary>Stable id so the same toast re-read on the next poll does not duplicate.</summary>
    public static string IdOf(string appId, string title, string body) =>
        $"toast:{Hash(appId)}:{Hash(title)}:{Hash(body)}";

    /// <summary>
    /// Builds the shell notification. Empty titles fall back to the app name so the card is never
    /// blank, and the body is trimmed to keep the list scannable.
    /// </summary>
    public static ShellNotification ToShell(MirroredToast toast)
    {
        var title = string.IsNullOrWhiteSpace(toast.Title) ? FallbackName(toast) : toast.Title.Trim();
        var body = (toast.Body ?? "").Trim();
        if (body.Length > MaxBody) body = body[..MaxBody].TrimEnd() + "…";

        return new ShellNotification(
            Id: IdOf(toast.AppId, title, body),
            AppId: string.IsNullOrWhiteSpace(toast.AppId) ? "windows" : toast.AppId,
            AppName: FallbackName(toast),
            Title: title,
            Body: body,
            Urgency: UrgencyOf(title, body),
            ReceivedAt: toast.ReceivedAt,
            Actions: Array.Empty<string>(),
            DeepLink: null);
    }

    /// <summary>
    /// Deliberately conservative: only obvious alarm words raise urgency, because marking everything
    /// high makes the DND rule useless.
    /// </summary>
    public static NotificationUrgency UrgencyOf(string? title, string? body)
    {
        var text = $"{title} {body}";
        foreach (var word in HighWords)
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase)) return NotificationUrgency.High;
        return NotificationUrgency.Normal;
    }

    public static string FallbackName(MirroredToast toast) =>
        string.IsNullOrWhiteSpace(toast.AppName)
            ? string.IsNullOrWhiteSpace(toast.AppId) ? "Windows" : toast.AppId
            : toast.AppName.Trim();

    private const int MaxBody = 240;

    private static readonly string[] HighWords = { "failed", "error", "critical", "security alert", "virus" };

    private static string Hash(string? value)
    {
        var hash = 1469598103934665603UL; // FNV-1a, stable across runs (string.GetHashCode is not)
        foreach (var c in value ?? "")
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return (hash & 0xFFFFFFFF).ToString("x8");
    }
}

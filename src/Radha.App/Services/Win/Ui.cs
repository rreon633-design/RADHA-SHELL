using Microsoft.UI.Dispatching;

namespace Radha.App.Services.Win;

/// <summary>Marshals service callbacks (sensor timers, media events) onto the UI thread.</summary>
internal static class Ui
{
    private static DispatcherQueue? _queue;

    public static void Initialize() => _queue = DispatcherQueue.GetForCurrentThread();

    public static void Post(Action action)
    {
        if (_queue is null || _queue.HasThreadAccess) action();
        else _queue.TryEnqueue(() => action());
    }
}

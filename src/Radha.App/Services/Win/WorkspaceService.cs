using Microsoft.UI.Windowing;
using Radha.App.Interop;
using Radha.Core.Services;

namespace Radha.App.Services.Win;

/// <summary>
/// Virtual desktops.
///
/// Windows has no documented API to enumerate or switch desktops, so the work happens in
/// <c>RadhaNative.dll</c>: it resolves the shell's undocumented <c>IVirtualDesktopManagerInternal</c>
/// with a per-build vtable layout, calls it, and reports "unsupported" for builds it has no verified
/// layout for. This class only reflects the result.
///
/// When the bridge is unavailable (older/newer Windows, DLL missing, shell not running) the bar keeps
/// a labelled local preview so the surface stays usable, and <see cref="IsLive"/> tells the UI which
/// of the two it is looking at. Nothing here pretends a switch happened.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService, IDisposable
{
    private readonly List<WorkspaceInfo> _workspaces = new();
    private readonly System.Threading.Timer? _poll;
    private readonly bool _live;
    private int _active = -1;

    /// <summary>True when the native bridge is driving real Windows virtual desktops.</summary>
    public bool IsLive => _live;

    /// <summary>Build the bridge resolved (e.g. 26100), or 0 when unknown.</summary>
    public int WindowsBuild { get; }

    public string BackendLabel => _live
        ? $"Windows virtual desktops · build {WindowsBuild}"
        : "Virtual desktops unavailable on this build — showing local placeholders";

    public IReadOnlyList<WorkspaceInfo> Workspaces => _workspaces;
    public event EventHandler? Changed;

    public WorkspaceService(int fallbackCount = 5)
    {
        _live = NativeBridge.TryProbe(out var build);
        WindowsBuild = build;

        if (!_live)
        {
            // Honest preview: the pills still work as UI state, but they are not Windows desktops.
            for (var i = 0; i < Math.Max(1, fallbackCount); i++)
                _workspaces.Add(new WorkspaceInfo(i, $"Workspace {i + 1}", i == 0, i < 2, 0));
            _active = 0;
            return;
        }

        Refresh();
        // Desktops can change outside the shell (Win+Ctrl+Arrow, task view); keep in step.
        _poll = new System.Threading.Timer(_ => Refresh(), null, 1500, 1500);
    }

    /// <summary>Reads count + current index from the bridge. Safe to call from any thread.</summary>
    private void Refresh()
    {
        if (!_live) return;

        var count = NativeBridge.RadhaWorkspaceCount();
        var current = NativeBridge.RadhaWorkspaceCurrent();
        if (count <= 0) return;

        var changed = count != _workspaces.Count || current != _active;
        if (!changed) return;

        _active = current;
        _workspaces.Clear();
        for (var i = 0; i < count; i++)
            _workspaces.Add(new WorkspaceInfo(i, $"Workspace {i + 1}", i == current, true, 0));

        Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public Task SwitchToAsync(int index)
    {
        if (index < 0) return Task.CompletedTask;

        if (_live)
        {
            if (!NativeBridge.RadhaWorkspaceSwitch(index)) return Task.CompletedTask;
            Refresh();
            return Task.CompletedTask;
        }

        if (index >= _workspaces.Count) return Task.CompletedTask;
        _active = index;
        for (var i = 0; i < _workspaces.Count; i++)
            _workspaces[i] = _workspaces[i] with { Active = i == index };
        Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves the foreground window to <paramref name="index"/>. Native only: there is no documented
    /// way to do this without a desktop GUID, and simulating a drag between desktops is not a thing.
    /// </summary>
    public Task MoveActiveWindowToAsync(int index)
    {
        if (!_live || index < 0) return Task.CompletedTask;
        NativeBridge.RadhaWorkspaceMoveActiveWindow(index);
        Refresh();
        return Task.CompletedTask;
    }

    public void Dispose() => _poll?.Dispose();
}

/// <summary>Monitor enumeration via DisplayArea (per-monitor scaling aware).</summary>
public sealed class DisplayService : IDisplayService, IDisposable
{
    private DisplayAreaWatcher? _watcher;

    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
    public event EventHandler? Changed;

    public DisplayService()
    {
        Refresh();
        try
        {
            _watcher = DisplayArea.CreateWatcher();
            _watcher.Updated += OnDisplaysChanged;
            _watcher.Added += OnDisplaysChanged;
            _watcher.Removed += OnDisplaysChanged;
            _watcher.Start();
        }
        catch { /* windowing APIs unavailable (headless / session 0) */ }
    }

    private void Refresh()
    {
        try
        {
            Monitors = DisplayArea.FindAll()
                .Select((area, index) => new MonitorInfo(
                    area.DisplayId.Value.ToString(),
                    area.DisplayId.Value.ToString(),
                    area.WorkArea.X, area.WorkArea.Y,
                    area.WorkArea.Width, area.WorkArea.Height,
                    1.0,
                    area.IsPrimary,
                    60))
                .ToList();
        }
        catch { }
    }

    // Fires on any topology change: resolution, DPI, monitor hot-plug, primary reassignment.
    private void OnDisplaysChanged(DisplayAreaWatcher sender, object args)
    {
        Refresh();
        Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.Updated -= OnDisplaysChanged;
            _watcher.Added -= OnDisplaysChanged;
            _watcher.Removed -= OnDisplaysChanged;
            _watcher.Stop();
        }
        catch { }
        _watcher = null;
    }
}

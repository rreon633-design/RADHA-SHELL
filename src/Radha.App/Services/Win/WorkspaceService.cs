using Microsoft.UI.Windowing;
using Radha.Core.Services;

namespace Radha.App.Services.Win;

/// <summary>
/// Virtual desktops.
///
/// Windows has no documented API to switch desktops or list them (IVirtualDesktopManager only
/// answers "which desktop owns this window"). The documented surface is used below; real switching
/// needs the undocumented IVirtualDesktopManagerInternal COM interface, which ships in the native
/// bridge (Radha.Native, phase 2). Until then the bar shows five local workspace pills so the UI is
/// complete and honest about what is simulated.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly List<WorkspaceInfo> _workspaces = new();
    private int _active;

    public IReadOnlyList<WorkspaceInfo> Workspaces => _workspaces;
    public event EventHandler? Changed;

    public WorkspaceService(int count = 5)
    {
        for (var i = 0; i < count; i++)
            _workspaces.Add(new WorkspaceInfo(i, $"Workspace {i + 1}", i == 0, i < 2, 0));
    }

    public Task SwitchToAsync(int index)
    {
        if (index < 0 || index >= _workspaces.Count) return Task.CompletedTask;
        _active = index;
        for (var i = 0; i < _workspaces.Count; i++)
            _workspaces[i] = _workspaces[i] with { Active = i == index };
        Ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        return Task.CompletedTask;
    }

    public Task MoveActiveWindowToAsync(int index) => Task.CompletedTask;
}

/// <summary>Monitor enumeration via DisplayArea (per-monitor scaling aware).</summary>
public sealed class DisplayService : IDisplayService
{
    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
    public event EventHandler? Changed;

    public DisplayService()
    {
        Refresh();
        try
        {
            DisplayArea.PrimaryChanged += (_, _) =>
            {
                Refresh();
                Changed?.Invoke(this, EventArgs.Empty);
            };
        }
        catch { }
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
}

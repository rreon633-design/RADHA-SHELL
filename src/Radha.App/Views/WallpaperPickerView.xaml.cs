using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Radha.App.Services.Win;
using Radha.App.ViewModels;
using Radha.Core.Config;
using Radha.Core.Services;
using Radha.Core.Wallpaper;

namespace Radha.App.Views;

/// <summary>
/// The wallpaper grid: thumbnails of everything in the wallpaper folder, a display selector that
/// decides which monitor the selection applies to, and the placement mode.
///
/// Control-driven rather than bound, like the settings window: the list is built once and mutated in
/// place, so there is no collection-change notification storm while a folder of photographs is
/// decoded.
/// </summary>
public sealed partial class WallpaperPickerView : UserControl
{
    private IWallpaperService? _wallpaper;
    private ShellConfig? _config;

    public WallpaperPickerView() => InitializeComponent();

    public void Configure(IWallpaperService wallpaper, ShellConfig config)
    {
        _wallpaper = wallpaper;
        _config = config;
        BuildFitBox();
        _ = RefreshAsync();
    }

    private void BuildFitBox()
    {
        FitBox.Items.Clear();
        foreach (var fit in new[] { WallpaperFit.Fill, WallpaperFit.Fit, WallpaperFit.Stretch, WallpaperFit.Center, WallpaperFit.Span })
            FitBox.Items.Add(WallpaperLibrary.ToConfigString(fit));

        var current = WallpaperLibrary.ToConfigString(WallpaperLibrary.ParseFit(_config?.Wallpaper.Fit));
        FitBox.SelectedIndex = Math.Max(0, FitBox.Items.IndexOf(current));
    }

    private async Task RefreshAsync()
    {
        if (_wallpaper is null) return;

        StatusText.Text = "Reading the wallpaper folder…";
        var entries = WallpaperLibrary.Scan(_config?.Paths.Wallpapers);

        // One pass, awaited sequentially: parallel decoding of 60 photographs flattens a laptop's
        // battery and the grid is not visible yet anyway.
        var tiles = new List<WallpaperTile>(entries.Count);
        foreach (var entry in entries)
        {
            var thumbnailPath = await ThumbnailPathAsync(entry);
            ImageSource? source = null;
            if (thumbnailPath is not null)
            {
                try { source = new BitmapImage(new Uri(thumbnailPath)); }
                catch { source = null; }
            }
            tiles.Add(new WallpaperTile(entry, source));
        }

        Tiles.ItemsSource = tiles;
        RefreshMonitors();

        StatusText.Text = entries.Count == 0
            ? $"No images in {_config?.Paths.Wallpapers}. Drop some in and press Refresh."
            : $"{entries.Count} image{(entries.Count == 1 ? "" : "s")} · click one to apply it to the selected display.";
    }

    private async Task<string?> ThumbnailPathAsync(WallpaperEntry entry)
    {
        if (_wallpaper is WallpaperService concrete) return await concrete.ThumbnailAsync(entry);
        return null;
    }

    private void RefreshMonitors()
    {
        if (_wallpaper is null) return;

        var previous = (MonitorBox.SelectedItem as MonitorChoice)?.Id;
        MonitorBox.Items.Clear();
        MonitorBox.Items.Add(new MonitorChoice("*", "All displays"));

        foreach (var monitor in _wallpaper.Monitors)
            MonitorBox.Items.Add(new MonitorChoice(monitor.Id, monitor.Name + (monitor.IsPrimary ? " · primary" : "")));

        // Keep the user's selection across a refresh when that display is still attached.
        var match = MonitorBox.Items.OfType<MonitorChoice>().FirstOrDefault(c => c.Id == previous);
        MonitorBox.SelectedItem = match ?? MonitorBox.Items[0];
    }

    private async void OnTileClick(object sender, ItemClickEventArgs e)
    {
        if (_wallpaper is null || e.ClickedItem is not WallpaperTile tile) return;

        var monitor = MonitorBox.SelectedItem as MonitorChoice;
        var target = monitor?.Id == "*" || monitor is null ? "all displays" : monitor.Label;

        StatusText.Text = $"Applying {tile.Name} to {target}…";

        var seed = monitor is null || monitor.Id == "*"
            ? await _wallpaper.SetAsync(tile.Path)
            : await _wallpaper.SetForMonitorAsync(monitor.Id, tile.Path);

        StatusText.Text = seed == default
            ? $"Windows refused the change for {target}."
            : $"Applied {tile.Name} to {target}.";
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_wallpaper is null) return;
        var seed = await _wallpaper.NextAsync();
        var current = _wallpaper.CurrentPath;
        StatusText.Text = seed == default || string.IsNullOrEmpty(current)
            ? "No wallpapers to cycle."
            : $"Now showing {Path.GetFileNameWithoutExtension(current)}.";
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnFitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wallpaper is null || FitBox.SelectedItem is not string fit) return;
        StatusText.Text = _wallpaper.SetFit(fit)
            ? $"Placement set to {fit}."
            : "This session does not accept placement changes.";
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        // Nothing to apply — the selector only decides where the next click lands.
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var folder = _config?.Paths.Wallpapers;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            StatusText.Text = "The wallpaper folder does not exist yet.";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch
        {
            StatusText.Text = "Could not open the folder.";
        }
    }

    private sealed record MonitorChoice(string Id, string Label);
}

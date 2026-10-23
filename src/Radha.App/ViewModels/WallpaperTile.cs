using Microsoft.UI.Xaml.Media;
using Radha.Core.Wallpaper;

namespace Radha.App.ViewModels;

/// <summary>
/// One cell in the wallpaper picker.
///
/// The thumbnail is an <see cref="ImageSource"/> rather than a path so the decode happens once, off
/// the UI thread, when the grid is built — binding a string would make every virtualised cell
/// re-open the file as it scrolls into view.
/// </summary>
public sealed class WallpaperTile
{
    public WallpaperTile(WallpaperEntry entry, ImageSource? thumbnail)
    {
        Entry = entry;
        Thumbnail = thumbnail;
    }

    public WallpaperEntry Entry { get; }

    public string Name => Entry.Name;

    public string Path => Entry.Path;

    public ImageSource? Thumbnail { get; }

    public string SizeLabel => Entry.Bytes >= 1024 * 1024
        ? $"{Entry.Bytes / (1024.0 * 1024.0):0.0} MB"
        : $"{Math.Max(1, Entry.Bytes / 1024)} KB";
}

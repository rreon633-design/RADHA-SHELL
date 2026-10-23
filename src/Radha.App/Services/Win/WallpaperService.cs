using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Radha.Core.Config;
using Radha.Core.Performance;
using Radha.Core.Services;
using Radha.Core.Theming;
using Windows.Graphics.Imaging;
using Radha.Core.Wallpaper;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Radha.App.Services.Win;

/// <summary>
/// Wallpaper listing / switching (SystemParametersInfo) and palette extraction
/// (Radha.Core decoder, WIC fallback for JPEG/WebP).
/// </summary>
public sealed class WallpaperService : IWallpaperService
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
    private readonly ShellConfig _config;
    private readonly PaletteCache _palettes = new(capacity: 16);
    private IReadOnlyList<DisplayTarget>? _monitors;
    private int _lastIndex = -1;

    public string? CurrentPath { get; private set; }

    /// <summary>Raised with the new path after a change, so the desktop layer can cross-fade.</summary>
    public event EventHandler<string>? Changed;

    public WallpaperService(ShellConfig config)
    {
        _config = config;
        CurrentPath = ReadCurrentWallpaper();
    }

    public IReadOnlyList<string> ListWallpapers()
    {
        var dir = _config.Paths.Wallpapers;
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public async Task<RgbSeed> SetAsync(string path)
    {
        if (!File.Exists(path)) return default;

        // The COM path understands "span" and keeps an existing per-monitor assignment consistent;
        // SystemParametersInfo is the fallback for a session where COM activation is unavailable.
        if (!DesktopWallpaper.SetAll(path))
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        CurrentPath = path;
        _config.Wallpaper.Path = path;
        _config.Wallpaper.Fit = WallpaperLibrary.ToConfigString(WallpaperLibrary.ParseFit(_config.Wallpaper.Fit));
        var seed = await ExtractSeedAsync(path);
        Changed?.Invoke(this, path);
        return new RgbSeed(seed.R, seed.G, seed.B);
    }

    // ---- per-monitor -------------------------------------------------------

    public IReadOnlyList<DisplayTarget> Monitors => _monitors ??= DesktopWallpaper.Monitors();

    public async Task<RgbSeed> SetForMonitorAsync(string monitorId, string path)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(monitorId)) return default;

        // Address the exact monitor; if the interface rejected the id (a stale assignment for a
        // display that has since been unplugged) nothing visible changes and we say so by returning
        // the default seed rather than silently repainting everything.
        if (!DesktopWallpaper.SetForMonitor(monitorId, path)) return default;

        _config.Wallpaper.PerMonitor[monitorId] = path;
        var seed = await ExtractSeedAsync(path);
        Changed?.Invoke(this, path);
        return new RgbSeed(seed.R, seed.G, seed.B);
    }

    public string Fit => WallpaperLibrary.ToConfigString(WallpaperLibrary.ParseFit(_config.Wallpaper.Fit));

    public bool SetFit(string fit)
    {
        var parsed = WallpaperLibrary.ParseFit(fit);
        if (!DesktopWallpaper.SetPosition(parsed)) return false;
        _config.Wallpaper.Fit = WallpaperLibrary.ToConfigString(parsed);
        return true;
    }

    // ---- thumbnails --------------------------------------------------------

    /// <summary>
    /// A cached thumbnail path for the picker, or null when the image cannot be decoded.
    ///
    /// Thumbnails are generated once per (path, mtime, size) and kept on disk, so opening the picker
    /// a second time does not re-decode a folder of 8-megapixel photographs. The shell asks Windows
    /// for the thumbnail rather than scaling in managed code, which also respects the user's
    /// per-format codec support.
    /// </summary>
    public async Task<string?> ThumbnailAsync(WallpaperEntry entry, int width = 240, int height = 135)
    {
        var cacheDirectory = Path.Combine(_config.Paths.Cache, "thumbs");
        var target = Path.Combine(cacheDirectory, WallpaperLibrary.ThumbnailKey(entry, width, height));

        try
        {
            if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
            Directory.CreateDirectory(cacheDirectory);

            var file = await StorageFile.GetFileFromPathAsync(entry.Path);
            using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.PicturesView, (uint)Math.Max(width, height));
            if (thumbnail is null || thumbnail.Size == 0) return null;

            var bytes = new byte[thumbnail.Size];
            using (var input = thumbnail.GetInputStreamAt(0))
            {
                using var reader = new DataReader(input);
                await reader.LoadAsync((uint)bytes.Length);
                reader.ReadBytes(bytes);
            }

            // Written to a temporary name first: a half-written PNG in the cache would be served
            // forever, because its key is derived from the source file and will not change.
            var temp = target + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Re-reads the monitor list after a display change (dock, undock, rotate).</summary>
    public void InvalidateMonitors() => _monitors = null;

    public async Task<RgbSeed> NextAsync()
    {
        var wallpapers = ListWallpapers();
        if (wallpapers.Count == 0) return default;
        _lastIndex = (_lastIndex + 1) % wallpapers.Count;
        return await SetAsync(wallpapers[_lastIndex]);
    }

    private async Task<Rgb> ExtractSeedAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension is ".png" or ".bmp")
            {
                // Cached by path + mtime + size: re-selecting a wallpaper is instant.
                return _palettes.SeedFor(path);
            }
        }
        catch { /* fall through to WIC */ }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);

            var bytes = new byte[4 * bitmap.PixelWidth * bitmap.PixelHeight];
            bitmap.CopyToBuffer(bytes.AsBuffer());
            var pixels = PixelBuffer.FromBgra(bitmap.PixelWidth, bitmap.PixelHeight, bytes);
            return WallpaperPalette.PickSeed(WallpaperPalette.Extract(pixels));
        }
        catch
        {
            return new Rgb(124, 92, 255);
        }
    }

    private static string? ReadCurrentWallpaper()
    {
        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)buffer.Capacity, buffer, 0))
                return buffer.ToString();
        }
        catch { }
        return null;
    }

    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, System.Text.StringBuilder pvParam, uint fWinIni);
}

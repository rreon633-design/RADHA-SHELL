using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Graphics.Canvas;
using Radha.App.Config;
using Radha.Core.Performance;
using Radha.Core.Services;
using Radha.Core.Theming;
using Windows.Graphics.Imaging;
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
    private int _lastIndex = -1;

    public string? CurrentPath { get; private set; }

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
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        CurrentPath = path;
        var seed = await ExtractSeedAsync(path);
        return new RgbSeed(seed.R, seed.G, seed.B);
    }

    public async Task<RgbSeed> NextAsync()
    {
        var wallpapers = ListWallpapers();
        if (wallpapers.Count == 0) return default;
        _lastIndex = (_lastIndex + 1) % wallpapers.Count;
        return await SetAsync(wallpapers[_lastIndex]);
    }

    private static async Task<Rgb> ExtractSeedAsync(string path)
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

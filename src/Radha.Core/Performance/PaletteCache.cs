using Radha.Core.Theming;

namespace Radha.Core.Performance;

/// <summary>
/// Memoises wallpaper palette extraction. Keys include size + last-write time, so an edited
/// wallpaper invalidates itself without a filesystem watcher.
/// </summary>
public sealed class PaletteCache
{
    private readonly LruCache<string, IReadOnlyList<PaletteCandidate>> _cache;

    public PaletteCache(int capacity = 24) =>
        _cache = new LruCache<string, IReadOnlyList<PaletteCandidate>>(capacity);

    public long Hits => _cache.Hits;
    public long Misses => _cache.Misses;
    public double HitRatio => _cache.HitRatio;
    public int Count => _cache.Count;

    public static string KeyFor(string path, DateTime lastWriteUtc, long length) =>
        $"{path}|{lastWriteUtc.Ticks}|{length}";

    public IReadOnlyList<PaletteCandidate> GetOrExtract(string path, int clusters = 8)
    {
        FileInfo info;
        try { info = new FileInfo(path); }
        catch { return Array.Empty<PaletteCandidate>(); }
        if (!info.Exists) return Array.Empty<PaletteCandidate>();

        var key = KeyFor(info.FullName, info.LastWriteTimeUtc, info.Length);
        if (_cache.TryGet(key, out var cached)) return cached;

        var pixels = ImageDecoder.DecodeFile(path);
        var result = WallpaperPalette.Extract(pixels, clusters);
        _cache.Put(key, result);
        return result;
    }

    public Rgb SeedFor(string path)
    {
        var candidates = GetOrExtract(path);
        return candidates.Count > 0 ? WallpaperPalette.PickSeed(candidates) : new Rgb(124, 92, 255);
    }

    /// <summary>Drops cached entries for a wallpaper path (any mtime).</summary>
    public void Invalidate(string path)
    {
        var full = Path.GetFullPath(path);
        var removed = 0;
        foreach (var key in _cache.KeysSnapshot())
        {
            if (!key.StartsWith(full + "|", StringComparison.Ordinal)) continue;
            if (_cache.TryRemove(key)) removed++;
        }
        if (removed == 0) _cache.Clear(); // key format changed or path normalized differently
    }
}

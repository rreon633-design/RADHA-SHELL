using System.Buffers.Binary;
using System.IO.Compression;

namespace Radha.Core.Theming;

/// <summary>A decoded image as tightly packed 8-bit RGB pixels.</summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgb { get; } // length = Width * Height * 3

    public PixelBuffer(int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("Pixel buffer size mismatch");
        Width = width;
        Height = height;
        Rgb = rgb;
    }

    /// <summary>Build from WIC/GSMTC BGRA8 pixels (used by the WinUI host for JPEG/WebP wallpapers).</summary>
    public static PixelBuffer FromBgra(int width, int height, ReadOnlySpan<byte> bgra, int stride = 0)
    {
        if (stride == 0) stride = width * 4;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var s = y * stride + x * 4;
                var d = (y * width + x) * 3;
                rgb[d] = bgra[s + 2];
                rgb[d + 1] = bgra[s + 1];
                rgb[d + 2] = bgra[s];
            }
        }
        return new PixelBuffer(width, height, rgb);
    }

    public Rgb this[int x, int y]
    {
        get
        {
            var i = (y * Width + x) * 3;
            return new Rgb(Rgb[i], Rgb[i + 1], Rgb[i + 2]);
        }
    }
}

/// <summary>
/// Zero-dependency wallpaper decoder for PNG (8-bit, non-interlaced) and BMP (24/32-bit).
/// JPEG/HEIC/WebP are decoded by the WinUI layer through WIC and fed in as a <see cref="PixelBuffer"/>,
/// keeping this engine portable and testable off-Windows.
/// </summary>
public static class ImageDecoder
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static PixelBuffer DecodeFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    public static PixelBuffer Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length > 8 && data[..8].SequenceEqual(PngSignature)) return DecodePng(data);
        if (data.Length > 2 && data[0] == (byte)'B' && data[1] == (byte)'M') return DecodeBmp(data);
        throw new NotSupportedException("Only PNG and BMP decode in Radha.Core; use WIC for JPEG/HEIC/WebP.");
    }

    // ---------------- PNG ----------------

    private static PixelBuffer DecodePng(ReadOnlySpan<byte> data)
    {
        var pos = 8;
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        using var idat = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            var type = System.Text.Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            var chunk = data.Slice(pos + 8, len);
            pos += 12 + len;

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                    height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                    bitDepth = chunk[8];
                    colorType = chunk[9];
                    interlace = chunk[12];
                    break;
                case "PLTE":
                    palette = chunk.ToArray();
                    break;
                case "IDAT":
                    idat.Write(chunk);
                    break;
                case "IEND":
                    pos = data.Length;
                    break;
            }
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG has no IHDR");
        if (bitDepth != 8) throw new NotSupportedException($"PNG bit depth {bitDepth} not supported (8 only)");
        if (interlace != 0) throw new NotSupportedException("Interlaced PNG not supported");

        var channels = colorType switch
        {
            0 => 1, // grayscale
            2 => 3, // RGB
            3 => 1, // palette index
            4 => 2, // gray + alpha
            6 => 4, // RGBA
            _ => throw new NotSupportedException($"PNG colour type {colorType} not supported")
        };
        if (colorType == 3 && palette is null) throw new InvalidDataException("Palette PNG without PLTE");

        idat.Position = 0;
        byte[] raw;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var outStream = new MemoryStream())
        {
            z.CopyTo(outStream);
            raw = outStream.ToArray();
        }

        var stride = width * channels;
        var expected = (stride + 1) * height;
        if (raw.Length < expected) throw new InvalidDataException("Truncated PNG image data");

        var lines = new byte[height][];
        var prev = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var line = new byte[stride];
            Array.Copy(raw, y * (stride + 1) + 1, line, 0, stride);
            Unfilter(filter, line, prev, channels);
            lines[y] = line;
            prev = line;
        }

        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var line = lines[y];
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 3;
                switch (colorType)
                {
                    case 0:
                    case 4:
                        var g = line[x * channels];
                        rgb[o] = rgb[o + 1] = rgb[o + 2] = g;
                        break;
                    case 2:
                    case 6:
                        var i = x * channels;
                        rgb[o] = line[i];
                        rgb[o + 1] = line[i + 1];
                        rgb[o + 2] = line[i + 2];
                        break;
                    case 3:
                        var pi = line[x] * 3;
                        if (palette is null || pi + 2 >= palette.Length) throw new InvalidDataException("Palette index out of range");
                        rgb[o] = palette[pi];
                        rgb[o + 1] = palette[pi + 1];
                        rgb[o + 2] = palette[pi + 2];
                        break;
                }
            }
        }

        return new PixelBuffer(width, height, rgb);
    }

    private static void Unfilter(byte filter, byte[] line, byte[] prev, int bpp)
    {
        switch (filter)
        {
            case 0: break;
            case 1:
                for (var i = bpp; i < line.Length; i++) line[i] = (byte)(line[i] + line[i - bpp]);
                break;
            case 2:
                for (var i = 0; i < line.Length; i++) line[i] = (byte)(line[i] + prev[i]);
                break;
            case 3:
                for (var i = 0; i < line.Length; i++)
                {
                    var left = i >= bpp ? line[i - bpp] : 0;
                    line[i] = (byte)(line[i] + ((left + prev[i]) >> 1));
                }
                break;
            case 4:
                for (var i = 0; i < line.Length; i++)
                {
                    var a = i >= bpp ? line[i - bpp] : 0;
                    var b = prev[i];
                    var c = i >= bpp ? prev[i - bpp] : 0;
                    line[i] = (byte)(line[i] + Paeth(a, b, c));
                }
                break;
            default: throw new InvalidDataException($"Unknown PNG filter {filter}");
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // ---------------- BMP ----------------

    private static PixelBuffer DecodeBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 54) throw new InvalidDataException("BMP too small");
        var offset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        var bpp = BinaryPrimitives.ReadInt16LittleEndian(data[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(data[30..]);

        if (compression != 0) throw new NotSupportedException("Compressed BMP not supported");
        if (bpp is not (24 or 32)) throw new NotSupportedException($"BMP {bpp}bpp not supported");

        var height = Math.Abs(rawHeight);
        var topDown = rawHeight < 0;
        var bytesPerPixel = bpp / 8;
        var rowStride = ((width * bytesPerPixel) + 3) / 4 * 4;
        var rgb = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            var srcY = topDown ? y : height - 1 - y;
            var rowStart = offset + srcY * rowStride;
            if (rowStart + width * bytesPerPixel > data.Length) throw new InvalidDataException("Truncated BMP");
            for (var x = 0; x < width; x++)
            {
                var s = rowStart + x * bytesPerPixel;
                var o = (y * width + x) * 3;
                rgb[o] = data[s + 2];     // BGR(A)
                rgb[o + 1] = data[s + 1];
                rgb[o + 2] = data[s];
            }
        }

        return new PixelBuffer(width, height, rgb);
    }
}

/// <summary>One representative colour cluster from a wallpaper.</summary>
public sealed record PaletteCandidate(Rgb Color, double Population, double Score)
{
    public OkLch Lch => Color.ToOkLch();
    public override string ToString() => $"{Color.ToHex()} pop={Population:P0} score={Score:F3}";
}

/// <summary>
/// Wallpaper → seed colour pipeline: sample pixels, k-means in OKLab,
/// then score clusters for "good primary" qualities (chroma, mid lightness,
/// enough population). Deterministic for a given image.
/// </summary>
public static class WallpaperPalette
{
    public static IReadOnlyList<PaletteCandidate> Extract(
        PixelBuffer image, int clusterCount = 8, int maxSamples = 12_000)
    {
        var samples = Sample(image, maxSamples);
        if (samples.Count == 0) return Array.Empty<PaletteCandidate>();

        var labs = new OkLab[samples.Count];
        for (var i = 0; i < samples.Count; i++) labs[i] = samples[i].ToOkLab();

        var centers = SeedCenters(labs, clusterCount);
        var counts = new int[centers.Length];
        var assignments = new int[labs.Length];

        for (var iter = 0; iter < 14; iter++)
        {
            Array.Clear(counts);
            var sumL = new double[centers.Length];
            var sumA = new double[centers.Length];
            var sumB = new double[centers.Length];

            for (var i = 0; i < labs.Length; i++)
            {
                var best = 0;
                var bestDist = double.MaxValue;
                for (var c = 0; c < centers.Length; c++)
                {
                    var dl = labs[i].L - centers[c].L;
                    var da = labs[i].A - centers[c].A;
                    var db = labs[i].Bb - centers[c].Bb;
                    var d = dl * dl + da * da + db * db;
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                assignments[i] = best;
                counts[best]++;
                sumL[best] += labs[i].L;
                sumA[best] += labs[i].A;
                sumB[best] += labs[i].Bb;
            }

            for (var c = 0; c < centers.Length; c++)
            {
                if (counts[c] == 0) continue;
                centers[c] = new OkLab(sumL[c] / counts[c], sumA[c] / counts[c], sumB[c] / counts[c]);
            }
        }

        var total = (double)labs.Length;
        var result = new List<PaletteCandidate>(centers.Length);
        for (var c = 0; c < centers.Length; c++)
        {
            if (counts[c] == 0) continue;
            var color = centers[c].ToRgb();
            var lch = color.ToOkLch();
            var pop = counts[c] / total;
            result.Add(new PaletteCandidate(color, pop, Score(lch, pop)));
        }

        return result.OrderByDescending(r => r.Score).ThenBy(r => r.Color.ToHex(), StringComparer.Ordinal).ToList();
    }

    public static Rgb PickSeed(IReadOnlyList<PaletteCandidate> candidates)
        => candidates.Count > 0 ? candidates[0].Color : new Rgb(124, 92, 255);

    /// <summary>Average colour, useful for the wallpaper-picker thumbnails and scrim tint.</summary>
    public static Rgb Average(PixelBuffer image)
    {
        long r = 0, g = 0, b = 0;
        var px = image.Width * image.Height;
        for (var i = 0; i < px; i++)
        {
            r += image.Rgb[i * 3];
            g += image.Rgb[i * 3 + 1];
            b += image.Rgb[i * 3 + 2];
        }
        return new Rgb((byte)(r / px), (byte)(g / px), (byte)(b / px));
    }

    private static List<Rgb> Sample(PixelBuffer image, int maxSamples)
    {
        var px = image.Width * image.Height;
        var step = Math.Max(1, px / Math.Max(1, maxSamples));
        var list = new List<Rgb>(Math.Min(px, maxSamples));

        // Deterministic stride sampling; skip pure black / blown-out whites which
        // are usually letterboxing, highlights or UI chrome rather than "the colour".
        for (var i = 0; i < px; i += step)
        {
            var o = i * 3;
            var c = new Rgb(image.Rgb[o], image.Rgb[o + 1], image.Rgb[o + 2]);
            var max = Math.Max(c.R, Math.Max(c.G, c.B));
            var min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (max <= 6) continue;
            if (min >= 248) continue;
            list.Add(c);
        }
        return list.Count > 0 ? list : Enumerable.Range(0, 1).Select(_ => new Rgb(124, 92, 255)).ToList();
    }

    private static OkLab[] SeedCenters(OkLab[] labs, int k)
    {
        if (labs.Length == 0) return Array.Empty<OkLab>();
        if (labs.Length == 1) return new[] { labs[0] };
        k = Math.Clamp(k, 2, Math.Min(16, labs.Length));
        var centers = new List<OkLab>(k);

        // Start from the most chromatic sample so the palette is not dragged grey,
        // then farthest-point seeding for the rest.
        var first = labs[0];
        var bestC = -1.0;
        foreach (var lab in labs)
        {
            var c = Math.Sqrt(lab.A * lab.A + lab.Bb * lab.Bb);
            if (c > bestC) { bestC = c; first = lab; }
        }
        centers.Add(first);

        while (centers.Count < k)
        {
            var bestIdx = 0;
            var bestDist = -1.0;
            for (var i = 0; i < labs.Length; i++)
            {
                var nearest = double.MaxValue;
                foreach (var c in centers)
                {
                    var dl = labs[i].L - c.L;
                    var da = labs[i].A - c.A;
                    var db = labs[i].Bb - c.Bb;
                    nearest = Math.Min(nearest, dl * dl + da * da + db * db);
                }
                if (nearest > bestDist) { bestDist = nearest; bestIdx = i; }
            }
            if (bestDist <= 1e-9) break;
            centers.Add(labs[bestIdx]);
        }

        return centers.ToArray();
    }

    private static double Score(OkLch lch, double population)
    {
        // Favour saturated, mid-light colours with a real presence in the image.
        var chromaScore = Math.Min(lch.C / 0.18, 1.0);
        var lightnessScore = 1.0 - Math.Abs(lch.L - 0.62) / 0.62;
        var popScore = Math.Min(population / 0.12, 1.0);
        return chromaScore * 0.55 + Math.Max(0, lightnessScore) * 0.25 + popScore * 0.20;
    }
}

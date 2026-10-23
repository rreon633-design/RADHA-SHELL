using System.Globalization;
using System.Text.Json;
using Radha.Core.Config;
using Radha.Core.Ipc;
using Radha.Core.Modules;
using Radha.Core.Theming;

namespace Radha.Cli;

/// <summary>
/// <c>radha</c> — the command-line surface of RADHA SHELL.
/// Pure logic lives here (writer-injected) so it can be exercised by tests;
/// <see cref="Program"/> only wires stdio and the exit code.
/// </summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            stdout.WriteLine(CommandCatalog.Help());
            return 0;
        }

        // Offline, daemon-free path: wallpaper → palette.
        if (args[0] == "scheme")
            return RunScheme(args, stdout, stderr);

        if (!CommandCatalog.TryParseModule(args[0], out var module))
        {
            stderr.WriteLine($"radha: unknown module '{args[0]}'");
            stderr.WriteLine(CommandCatalog.Help());
            return 1;
        }

        var command = args.Length > 1 ? args[1] : "toggle";
        if (!CommandCatalog.IsValid(module, command))
        {
            stderr.WriteLine($"radha: '{command}' is not a valid command for module '{module.ToString().ToLowerInvariant()}'");
            stderr.WriteLine($"       valid: {string.Join(" | ", CommandCatalog.For(module))}");
            return 1;
        }

        var commandArgs = args.Skip(2).ToArray();

        await using var client = new IpcClient(IpcEndpoints.Default());
        try
        {
            await client.ConnectAsync(TimeSpan.FromSeconds(2), ct);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"radha: RADHA SHELL is not running ({ex.GetType().Name}). Start Radha.App first.");
            return 2;
        }

        var response = await client.SendCommandAsync(module.ToString().ToLowerInvariant(), command, commandArgs);
        if (!response.Ok)
        {
            stderr.WriteLine($"radha: {response.Error}");
            return 3;
        }

        if (!string.IsNullOrEmpty(response.Payload)) stdout.WriteLine(response.Payload);
        return 0;
    }

    private static int RunScheme(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 3 || args[1] is not ("extract" or "from"))
        {
            stderr.WriteLine("usage: radha scheme extract <image.png|bmp> [--variant tonalSpot|vibrant|expressive|neutral|monochrome] [--light|--dark] [--json]");
            return 1;
        }

        var path = args[2];
        if (!File.Exists(path))
        {
            stderr.WriteLine($"radha: no such image: {path}");
            return 1;
        }

        var variant = SchemeVariant.TonalSpot;
        var dark = true;
        var json = false;
        for (var i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--light": dark = false; break;
                case "--dark": dark = true; break;
                case "--json": json = true; break;
                case "--variant" when i + 1 < args.Length:
                    if (!Enum.TryParse<SchemeVariant>(args[++i], ignoreCase: true, out variant))
                    {
                        stderr.WriteLine($"radha: unknown variant '{args[i]}'");
                        return 1;
                    }
                    break;
            }
        }

        PixelBuffer image;
        try
        {
            image = ImageDecoder.DecodeFile(path);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"radha: could not decode image — {ex.Message}");
            return 1;
        }

        var candidates = WallpaperPalette.Extract(image);
        var seed = WallpaperPalette.PickSeed(candidates);
        var scheme = MaterialScheme.FromSeed(seed, dark, variant);

        if (json)
        {
            var payload = new
            {
                image = Path.GetFileName(path),
                size = $"{image.Width}x{image.Height}",
                average = WallpaperPalette.Average(image).ToHex(),
                seed = seed.ToHex(),
                dark,
                variant = variant.ToString(),
                roles = scheme.ToRoleMap().ToDictionary(kv => kv.Key, kv => kv.Value.ToHex()),
                candidates = candidates.Select(c => new { hex = c.Color.ToHex(), score = Math.Round(c.Score, 4), population = Math.Round(c.Population, 4) })
            };
            stdout.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        stdout.WriteLine($"RADHA SHELL · palette from {Path.GetFileName(path)} ({image.Width}×{image.Height})");
        stdout.WriteLine($"seed {seed.ToHex()}   variant {variant}   {(dark ? "dark" : "light")}");
        stdout.WriteLine();
        foreach (var (role, color) in scheme.ToRoleMap())
        {
            stdout.WriteLine($"  \u001b[48;2;{color.R};{color.G};{color.B}m      \u001b[0m  {role,-22} {color.ToHex()}");
        }
        stdout.WriteLine();
        stdout.WriteLine("candidates:");
        foreach (var c in candidates.Take(6))
            stdout.WriteLine($"  {c.Color.ToHex()}  score {c.Score:F3}  pop {c.Population.ToString("P0", CultureInfo.InvariantCulture)}");
        return 0;
    }
}

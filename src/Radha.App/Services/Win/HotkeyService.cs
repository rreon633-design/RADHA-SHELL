using System.IO.Pipes;
using System.Text;
using Radha.App.Interop;
using Radha.Core.Modules;

namespace Radha.App.Services.Win;

/// <summary>
/// Global hotkeys.
///
/// <c>RadhaNative.dll</c> owns the hidden sink window and the <c>WM_HOTKEY</c> loop, but it does
/// not act on a key itself: it writes <c>"module command"</c> into a dedicated pipe. This service
/// parses those lines, validates them against <see cref="CommandCatalog"/> and hands them to the
/// shell. A key press can therefore never reach anything the CLI could not reach.
///
/// The pipe is separate from the JSON IPC endpoint on purpose — the native writer is a plain
/// UTF-16 line and must not be able to speak the binary protocol the client uses.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    public const string PipeName = "radha-shell-hotkeys";

    // RegisterHotKey modifiers
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Default bindings, mirroring the native id table in Hotkeys.cpp.</summary>
    public static readonly (int Id, string Module, string Command, uint Modifiers, uint Key, string Label)[] Defaults =
    {
        (1, "launcher", "toggle", ModControl | ModAlt, 0x20, "Ctrl+Alt+Space"),
        (2, "dashboard", "toggle", ModControl | ModAlt, 0x44, "Ctrl+Alt+D"),
        (3, "controlcenter", "toggle", ModControl | ModAlt, 0x43, "Ctrl+Alt+C"),
        (4, "notifications", "toggle", ModControl | ModAlt, 0x4E, "Ctrl+Alt+N"),
        (5, "wallpaper", "next", ModControl | ModAlt, 0x57, "Ctrl+Alt+W"),
        (6, "session", "menu", ModControl | ModAlt, 0x51, "Ctrl+Alt+Q")
    };

    private readonly Func<string, string, Task> _dispatch;
    private readonly List<int> _registered = new();
    private readonly List<string> _failures = new();
    private CancellationTokenSource? _cts;
    private Task? _listener;

    public HotkeyService(Func<string, string, Task> dispatch) => _dispatch = dispatch;

    public string PipeEndpoint => $@"\\.\pipe\{PipeName}";
    public IReadOnlyList<int> Registered => _registered;
    public IReadOnlyList<string> Failures => _failures;
    public bool IsRunning => _listener is { IsCompleted: false };

    /// <summary>Binds every default hotkey. Returns how many the OS accepted.</summary>
    public int Start()
    {
        if (!OperatingSystem.IsWindows()) return 0;

        _cts = new CancellationTokenSource();
        _listener = Task.Run(() => ListenAsync(_cts.Token));

        try { NativeBridge.RadhaHotkeySetPipeEndpoint(PipeName); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _failures.Add("native bridge unavailable: " + ex.GetType().Name);
            return 0;
        }

        foreach (var binding in Defaults)
        {
            try
            {
                if (NativeBridge.RadhaHotkeyRegister(binding.Id, binding.Modifiers, binding.Key))
                    _registered.Add(binding.Id);
                else
                    _failures.Add($"{binding.Label}: already taken by another application");
            }
            catch (Exception ex)
            {
                _failures.Add($"{binding.Label}: {ex.GetType().Name}");
            }
        }

        return _registered.Count;
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var buffer = new MemoryStream();
                var chunk = new byte[1024];
                int read;
                while ((read = await pipe.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > 4096) break;   // a hotkey line is a few dozen bytes
                }

                var text = Encoding.Unicode.GetString(buffer.ToArray());
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    await HandleLineAsync(line.Trim(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { /* client vanished mid-write; next connection will work */ }
            catch (UnauthorizedAccessException ex) { _failures.Add("pipe: " + ex.Message); break; }
            finally
            {
                if (pipe is not null) { await pipe.DisposeAsync().ConfigureAwait(false); }
            }
        }
    }

    private async Task HandleLineAsync(string line, CancellationToken ct)
    {
        if (line.Length == 0) return;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var module = parts[0];
        var command = parts.Length > 1 ? parts[1] : "toggle";

        // Same gate as the CLI: unknown module or command is dropped, not guessed.
        if (!CommandCatalog.TryParseModule(module, out var parsed)) return;
        if (!CommandCatalog.IsValid(parsed, command)) return;

        try { await _dispatch(parts[0], command).ConfigureAwait(false); }
        catch (Exception) { /* a hotkey must never take the shell down */ }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }

        if (OperatingSystem.IsWindows())
        {
            foreach (var id in _registered)
            {
                try { NativeBridge.RadhaHotkeyUnregister(id); } catch { }
            }
        }

        _registered.Clear();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }
}

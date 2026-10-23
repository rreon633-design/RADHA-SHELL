using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radha.Core.Ipc;

/// <summary>One CLI → shell command. Mirrors <c>radha &lt;module&gt; &lt;command&gt; [args]</c>.</summary>
public sealed record IpcRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("module")] string Module,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("args")] string[] Args);

public sealed record IpcResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("payload")] string? Payload,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("ms")] double ElapsedMs)
{
    public static IpcResponse Ok_(string id, string? payload = null, double ms = 0) => new(id, true, payload, null, ms);
    public static IpcResponse Fail(string id, string error, double ms = 0) => new(id, false, null, error, ms);
}

public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Endpoint naming shared by daemon and CLI.
/// Windows → named pipe <c>\\.\pipe\radha-shell</c>;
/// other OSes → Unix domain socket (used by the test-suite and for development).
/// </summary>
public static class IpcEndpoints
{
    public const string WindowsPipeName = "radha-shell";

    public static string Default()
    {
        if (OperatingSystem.IsWindows()) return $@"\\.\pipe\{WindowsPipeName}";
        var dir = Path.Combine(Path.GetTempPath(), "radha");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "radha-shell.sock");
    }
}

/// <summary>Length-prefixed JSON framing over any bidirectional stream.</summary>
public static class IpcFraming
{
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, IpcRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(request, IpcJson.Options);
        await WriteFrameAsync(stream, json, ct).ConfigureAwait(false);
    }

    public static async Task WriteAsync(Stream stream, IpcResponse response, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, IpcJson.Options);
        await WriteFrameAsync(stream, json, ct).ConfigureAwait(false);
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        if (payload.Length > MaxMessageBytes)
            throw new InvalidOperationException($"IPC message too large ({payload.Length} bytes)");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<IpcRequest?> ReadRequestAsync(Stream stream, CancellationToken ct = default)
    {
        var payload = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
        return payload is null ? null : JsonSerializer.Deserialize<IpcRequest>(payload, IpcJson.Options);
    }

    public static async Task<IpcResponse?> ReadResponseAsync(Stream stream, CancellationToken ct = default)
    {
        var payload = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
        return payload is null ? null : JsonSerializer.Deserialize<IpcResponse>(payload, IpcJson.Options);
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var read = await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);
        if (!read) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Invalid IPC frame length {length}");

        var payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, ct).ConfigureAwait(false))
            throw new EndOfStreamException("Truncated IPC frame");
        return payload;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (offset == 0) return false;            // clean EOF between frames
                throw new EndOfStreamException($"Truncated IPC frame after {offset} bytes");
            }
            offset += n;
        }
        return true;
    }
}

/// <summary>
/// Shell daemon endpoint. One handler processes every request; concurrency is per-client.
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly string _endpoint;
    private readonly Func<IpcRequest, CancellationToken, Task<IpcResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Socket? _listener;
    private Task? _acceptLoop;
    private readonly List<Task> _clients = new();
    private bool _disposed;

    public IpcServer(string? endpoint, Func<IpcRequest, CancellationToken, Task<IpcResponse>> handler)
    {
        _endpoint = endpoint ?? IpcEndpoints.Default();
        _handler = handler;
    }

    public string Endpoint => _endpoint;

    public Task StartAsync()
    {
        _acceptLoop ??= Task.Run(AcceptLoopAsync);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await AcceptNamedPipesAsync(_cts.Token).ConfigureAwait(false);
            return;
        }

        if (File.Exists(_endpoint)) File.Delete(_endpoint);
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_endpoint));
        _listener.Listen(16);

        while (!_cts.IsCancellationRequested)
        {
            Socket client;
            try { client = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }

            var task = HandleClientAsync(client, _cts.Token);
            lock (_clients) _clients.Add(task);
        }
    }

    private async Task AcceptNamedPipesAsync(CancellationToken ct)
    {
#if WINDOWS
        while (!ct.IsCancellationRequested)
        {
            var pipe = new System.IO.Pipes.NamedPipeServerStream(
                IpcEndpoints.WindowsPipeName,
                System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances,
                System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            var task = HandleStreamAsync(pipe, ct);
            lock (_clients) _clients.Add(task);
        }
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Named pipes are Windows-only; use a Unix socket endpoint.");
#endif
    }

    private async Task HandleClientAsync(Socket socket, CancellationToken ct)
    {
        await using var network = new NetworkStream(socket, ownsSocket: true);
        await HandleStreamAsync(network, ct).ConfigureAwait(false);
    }

    private async Task HandleStreamAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await IpcFraming.ReadRequestAsync(stream, ct).ConfigureAwait(false);
                if (request is null) return;

                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                IpcResponse response;
                try
                {
                    response = await _handler(request, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    response = IpcResponse.Fail(request.Id, ex.Message);
                }
                var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                              System.Diagnostics.Stopwatch.Frequency;
                response = response with { ElapsedMs = Math.Round(elapsed, 3) };

                await IpcFraming.WriteAsync(stream, response, ct).ConfigureAwait(false);
            }
        }
        catch (EndOfStreamException) { /* client vanished */ }
        catch (IOException) { /* pipe broke */ }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;      // allow `await using` plus an explicit dispose
        _disposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener?.Dispose();
        Task[] pending;
        lock (_clients) pending = _clients.ToArray();
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { /* shutdown is best-effort */ }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
        }
        if (!OperatingSystem.IsWindows() && File.Exists(_endpoint))
        {
            try { File.Delete(_endpoint); } catch { }
        }
        _cts.Dispose();
    }
}

/// <summary>Thin client used by <c>radha.exe</c> and by tests.</summary>
public sealed class IpcClient : IAsyncDisposable
{
    private readonly string _endpoint;
    private Stream? _stream;

    public IpcClient(string? endpoint = null) => _endpoint = endpoint ?? IpcEndpoints.Default();

    public async Task ConnectAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(2);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(limit);

        if (OperatingSystem.IsWindows())
        {
#if WINDOWS
            var pipe = new System.IO.Pipes.NamedPipeClientStream(
                ".", IpcEndpoints.WindowsPipeName, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)limit.TotalMilliseconds, linked.Token).ConfigureAwait(false);
            _stream = pipe;
            return;
#else
            throw new PlatformNotSupportedException("Named pipes are Windows-only.");
#endif
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), linked.Token).ConfigureAwait(false);
            _stream = new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<IpcResponse> SendAsync(IpcRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("IpcClient is not connected.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

        await IpcFraming.WriteAsync(_stream, request, linked.Token).ConfigureAwait(false);
        var response = await IpcFraming.ReadResponseAsync(_stream, linked.Token).ConfigureAwait(false);
        return response ?? throw new EndOfStreamException("Shell closed the IPC connection.");
    }

    public Task<IpcResponse> SendCommandAsync(string module, string command, params string[] args)
        => SendAsync(new IpcRequest(Guid.NewGuid().ToString("N")[..8], module, command, args));

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false);
        _stream = null;
    }
}

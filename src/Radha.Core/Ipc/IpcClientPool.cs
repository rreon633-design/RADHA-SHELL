namespace Radha.Core.Ipc;

/// <summary>
/// Reuses one connection for a burst of commands (the CLI issues one, the launcher many).
/// Keeps the daemon's per-client rate budget meaningful instead of burning it on reconnects.
/// </summary>
public sealed class IpcClientPool : IAsyncDisposable
{
    private readonly string? _endpoint;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IpcClient? _client;

    public IpcClientPool(string? endpoint = null) => _endpoint = endpoint;

    public async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _client ??= await ConnectAsync(ct).ConfigureAwait(false);
            try
            {
                return await _client.SendAsync(request, ct: ct).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                // Daemon restarted: reconnect once, then retry.
                await _client.DisposeAsync().ConfigureAwait(false);
                _client = await ConnectAsync(ct).ConfigureAwait(false);
                return await _client.SendAsync(request, ct: ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IpcClient> ConnectAsync(CancellationToken ct)
    {
        var client = new IpcClient(_endpoint);
        await client.ConnectAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        _client = null;
        _gate.Dispose();
    }
}

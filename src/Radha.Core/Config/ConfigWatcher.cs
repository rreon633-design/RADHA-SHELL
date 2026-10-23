namespace Radha.Core.Config;

/// <summary>
/// Watches <c>shell.json</c> and raises a debounced change notification.
///
/// Editors save in bursts (write, rename, chmod) and some write through a temporary file, so a
/// raw <see cref="FileSystemWatcher"/> event is not a reliable "the config is now valid" signal.
/// This type coalesces bursts and verifies the file parses before raising anything.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private DateTime _lastWrite;
    private bool _disposed;

    public ConfigWatcher(string path, TimeSpan? debounce = null)
    {
        _path = path;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Raised after a settled write, on a background thread. Never raised for invalid JSON.</summary>
    public event Action<string>? Changed;

    /// <summary>Raised when the file changed but could not be read — the shell keeps the old config.</summary>
    public event Action<Exception>? Rejected;

    public bool Enabled => _watcher?.EnableRaisingEvents == true;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watcher is not null) return;

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false
        };

        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Schedule();

    private void Schedule()
    {
        lock (_gate)
        {
            _lastWrite = DateTime.UtcNow;
            _timer ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        string text;
        try
        {
            // A reader that opened the file mid-write can see a partial document; retry briefly.
            text = ReadWithRetry();
        }
        catch (Exception ex)
        {
            Rejected?.Invoke(ex);
            return;
        }

        try
        {
            System.Text.Json.Nodes.JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            Rejected?.Invoke(ex);
            return;
        }

        Changed?.Invoke(text);
    }

    private string ReadWithRetry()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}

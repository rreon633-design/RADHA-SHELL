using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using Radha.Core.Services;

namespace Radha.App.Services.Win;

/// <summary>
/// WASAPI loopback capture → FFT spectrum, plus endpoint volume / mute.
/// Replaces cava + pipewire in the Caelestia stack.
/// </summary>
public sealed class AudioService : IAudioService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _gate = new();
    private readonly float[] _spectrum;
    private MMDevice? _render;
    private MMDevice? _captureDevice;
    private WasapiLoopbackCapture? _loopback;
    private Complex[] _buffer = Array.Empty<Complex>();

    public int Volume
    {
        get
        {
            try { return (int)Math.Round((_render?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0.5f) * 100); }
            catch { return 50; }
        }
    }

    public bool Muted
    {
        get
        {
            try { return _render?.AudioEndpointVolume.Mute ?? false; }
            catch { return false; }
        }
    }

    public bool MicMuted
    {
        get
        {
            try { return _captureDevice?.AudioEndpointVolume.Mute ?? false; }
            catch { return false; }
        }
    }

    public event EventHandler? Changed;

    public AudioService(int bars = 48)
    {
        _spectrum = new float[Math.Max(8, bars)];
        try
        {
            _render = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _captureDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            _loopback = new WasapiLoopbackCapture(_render);
            _loopback.DataAvailable += OnDataAvailable;
            _loopback.StartRecording();
        }
        catch
        {
            // No audio endpoint (RDP / CI machines): spectrum stays flat.
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded < 8) return;
        var samples = new float[e.BytesRecorded / 4];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        var count = Math.Min(1024, samples.Length);
        if (_buffer.Length != count)
        {
            var m = 1;
            while (m * 2 <= count) m *= 2;
            _buffer = new Complex[m];
        }

        var offset = samples.Length - _buffer.Length;
        for (var i = 0; i < _buffer.Length; i++)
        {
            var window = (float)FastFourierTransform.HannWindow(i, _buffer.Length);
            _buffer[i].X = samples[offset + i] * window;
            _buffer[i].Y = 0;
        }

        var log2 = (int)Math.Log2(_buffer.Length);
        FastFourierTransform.FFT(true, log2, _buffer);

        // Log-spaced binning into the display bars.
        lock (_gate)
        {
            var bars = _spectrum.Length;
            for (var bar = 0; bar < bars; bar++)
            {
                var start = (int)((Math.Pow(1.045, bar) - 1) * 2);
                var end = Math.Max(start + 1, (int)((Math.Pow(1.045, bar + 1) - 1) * 2));
                end = Math.Min(end, _buffer.Length / 2);

                var peak = 0f;
                for (var i = Math.Min(start, _buffer.Length / 2 - 1); i < end; i++)
                {
                    var magnitude = (float)Math.Sqrt(_buffer[i].X * _buffer[i].X + _buffer[i].Y * _buffer[i].Y);
                    peak = Math.Max(peak, magnitude);
                }

                var normalized = Math.Clamp((float)Math.Log10(1 + peak * 1200), 0, 1);
                _spectrum[bar] = _spectrum[bar] * 0.72f + normalized * 0.28f;
            }
        }
    }

    public ReadOnlySpan<float> GetSpectrum()
    {
        lock (_gate) return _spectrum.AsSpan();
    }

    public void SetVolume(int percent)
    {
        try
        {
            if (_render is null) return;
            _render.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    public void ToggleMute()
    {
        try
        {
            if (_render is null) return;
            _render.AudioEndpointVolume.Mute = !_render.AudioEndpointVolume.Mute;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    public void ToggleMic()
    {
        try
        {
            if (_captureDevice is null) return;
            _captureDevice.AudioEndpointVolume.Mute = !_captureDevice.AudioEndpointVolume.Mute;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    public void Dispose()
    {
        try { _loopback?.StopRecording(); } catch { }
        _loopback?.Dispose();
        _render?.Dispose();
        _captureDevice?.Dispose();
        _enumerator.Dispose();
    }
}

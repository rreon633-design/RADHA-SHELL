using System.Collections.Specialized;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Radha.App.Controls;

/// <summary>
/// Win2D sparkline used by the performance cards (CPU / GPU / memory).
/// Values are normalised 0..100; the control redraws on collection changes so
/// the graph animates at sensor rate without touching the UI thread layout.
/// </summary>
public sealed class LineGraph : UserControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<double>), typeof(LineGraph),
        new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(LineGraph),
        new PropertyMetadata(Color.FromArgb(255, 160, 140, 255), OnAccentChanged));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(LineGraph),
        new PropertyMetadata(100d));

    private readonly CanvasControl _canvas;

    public LineGraph()
    {
        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        Content = _canvas;
    }

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LineGraph)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.OnCollectionChanged;
        control._canvas?.Invalidate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _canvas.Invalidate();

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LineGraph)d)._canvas.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        if (width < 4 || height < 4) return;

        var values = Values?.ToArray() ?? Array.Empty<double>();
        if (values.Length < 2) return;

        var max = Math.Max(1, Math.Min(MaxValue, values.Max() * 1.15));
        var stepX = width / (values.Length - 1);

        using var geometry = new CanvasPathBuilder(sender);
        geometry.BeginFigure(0, height);
        for (var i = 0; i < values.Length; i++)
        {
            var x = stepX * i;
            var y = height - (float)Math.Clamp(values[i] / max, 0, 1) * height;
            geometry.AddLine(x, y);
        }
        geometry.EndFigure(true);

        using var path = CanvasGeometry.CreatePath(geometry);
        var fillColor = Color.FromArgb(38, Accent.R, Accent.G, Accent.B);
        session.FillGeometry(path, fillColor);
        session.DrawGeometry(path, Color.FromArgb(210, Accent.R, Accent.G, Accent.B), 1.8f);
    }
}

/// <summary>48-bar audio spectrum driven by NAudio loopback FFT.</summary>
public sealed class SpectrumBars : UserControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<double>), typeof(SpectrumBars),
        new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(SpectrumBars),
        new PropertyMetadata(Color.FromArgb(255, 160, 140, 255)));

    private readonly CanvasControl _canvas;

    public SpectrumBars()
    {
        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        Content = _canvas;
    }

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SpectrumBars)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.OnCollectionChanged;
        control._canvas?.Invalidate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _canvas.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;
        var values = Values?.ToArray() ?? Array.Empty<double>();
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        if (values.Length == 0 || width < 4 || height < 4) return;

        var gap = 3f;
        var barWidth = Math.Max(1.5f, (width - gap * (values.Length - 1)) / values.Length);

        for (var i = 0; i < values.Length; i++)
        {
            var normalized = (float)Math.Clamp(values[i], 0, 1);
            var barHeight = Math.Max(3f, normalized * height);
            var x = i * (barWidth + gap);
            var y = height - barHeight;

            var alpha = (byte)(120 + normalized * 120);
            using var brush = new CanvasLinearGradientBrush(sender,
                Color.FromArgb(alpha, Accent.R, Accent.G, Accent.B),
                Color.FromArgb(alpha, (byte)Math.Min(255, Accent.R + 40), Accent.G, 255))
            {
                StartPoint = new System.Numerics.Vector2(x, y),
                EndPoint = new System.Numerics.Vector2(x, height)
            };
            session.FillRoundedRectangle(x, y, barWidth, barHeight, barWidth / 2, barWidth / 2, brush);
        }
    }
}

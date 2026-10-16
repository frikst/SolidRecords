using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SolidRecords.Core;
using SolidRecords.Core.Recording;

namespace SolidRecords.App.Presentation;

public sealed class TimelineControl : FrameworkElement
{
    private readonly List<(Rect Bounds, ActivityInterval Interval)> _hitAreas = [];
    private static readonly string[] Palette = ["#5577ED", "#21A8A0", "#AC6BD2", "#E4A343", "#DF6C88", "#4E9EC5"];
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items),
        typeof(IReadOnlyList<ActivityInterval>), typeof(TimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush),
        typeof(Brush), typeof(TimelineControl), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(nameof(TickBrush),
        typeof(Brush), typeof(TimelineControl), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(nameof(LabelBrush),
        typeof(Brush), typeof(TimelineControl), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public TimelineControl()
    {
        SetResourceReference(TrackBrushProperty, "ControlAltFillColorTertiaryBrush");
        SetResourceReference(TickBrushProperty, "ControlStrokeColorSecondaryBrush");
        SetResourceReference(LabelBrushProperty, "TextFillColorSecondaryBrush");
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush TickBrush
    {
        get => (Brush)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IReadOnlyList<ActivityInterval>? Items
    {
        get => (IReadOnlyList<ActivityInterval>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        _hitAreas.Clear();
        var width = ActualWidth;
        if (width < 1) return;
        var items = Items ?? [];
        var start = items.Count > 0 ? items.Min(x => x.Start).ToLocalTime() : new DateTimeOffset(DateTime.Today.AddHours(6));
        var end = items.Count > 0 ? items.Max(x => x.End).ToLocalTime() : start.AddHours(12);
        var from = start.AddMinutes(-start.Minute).AddSeconds(-start.Second).AddMilliseconds(-start.Millisecond);
        var to = end.AddHours(1).AddMinutes(-end.Minute).AddSeconds(-end.Second).AddMilliseconds(-end.Millisecond);
        if (to - from < TimeSpan.FromHours(2)) to = from.AddHours(2);
        var seconds = (to - from).TotalSeconds;
        drawing.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 2, width, 16), 3, 3);
        foreach (var item in items)
        foreach (var span in ActivityLogPolicy.GetSpans(item))
        {
            var x = (span.Start - from).TotalSeconds / seconds * width;
            var w = Math.Max(1.5, (span.End - span.Start).TotalSeconds / seconds * width);
            var bounds = new Rect(Math.Max(0, x), 2, Math.Min(w, width - Math.Max(0, x)), 16);
            drawing.DrawRectangle(BrushFor(item.Window.Application), null, bounds);
            _hitAreas.Add((bounds, item));
        }
        var step = Math.Max(1, (int)Math.Ceiling((to - from).TotalHours / Math.Max(2, width / 95)));
        for (var tick = from; tick <= to; tick = tick.AddHours(step))
        {
            var x = (tick - from).TotalSeconds / seconds * width;
            drawing.DrawLine(new Pen(TickBrush, 1), new(x, 20), new(x, 24));
            var text = new FormattedText(tick.ToString("HH:mm"), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, width - text.Width)), 27));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var hit = _hitAreas.FirstOrDefault(x => x.Bounds.Contains(point));
        ToolTip = hit.Interval is null ? null : $"{hit.Interval.Window.Application} · {hit.Interval.Window.Title}\n" +
            $"{hit.Interval.Start.ToLocalTime():HH:mm:ss} – {hit.Interval.End.ToLocalTime():HH:mm:ss} · {ActivityRow.FormatDuration(hit.Interval.Duration)}";
    }

    private static Brush BrushFor(string application)
    {
        uint hash = 2166136261;
        foreach (var c in application) hash = (hash ^ c) * 16777619;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[hash % (uint)Palette.Length]));
    }
}

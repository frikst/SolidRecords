using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SolidRecords.Core;

namespace SolidRecords.App.Presentation;

public sealed class TimelineControl : FrameworkElement
{
    private readonly List<(Rect Bounds, ActivityInterval Interval)> _hitAreas = [];
    private static readonly string[] Palette = ["#5577ED", "#21A8A0", "#AC6BD2", "#E4A343", "#DF6C88", "#4E9EC5"];
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items),
        typeof(IReadOnlyList<ActivityInterval>), typeof(TimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

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
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(242, 245, 250)), null, new Rect(0, 6, width, 30), 4, 4);
        foreach (var item in items)
        {
            var x = (item.Start - from).TotalSeconds / seconds * width;
            var w = Math.Max(1.5, item.Duration.TotalSeconds / seconds * width);
            var bounds = new Rect(Math.Max(0, x), 6, Math.Min(w, width - Math.Max(0, x)), 30);
            drawing.DrawRectangle(BrushFor(item.Window.Application), null, bounds);
            _hitAreas.Add((bounds, item));
        }
        var step = Math.Max(1, (int)Math.Ceiling((to - from).TotalHours / Math.Max(2, width / 95)));
        for (var tick = from; tick <= to; tick = tick.AddHours(step))
        {
            var x = (tick - from).TotalSeconds / seconds * width;
            drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(221, 228, 238)), 1), new(x, 38), new(x, 44));
            var text = new FormattedText(tick.ToString("HH:mm"), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11,
                new SolidColorBrush(Color.FromRgb(104, 117, 138)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, width - text.Width)), 48));
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

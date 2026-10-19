using System.Windows;
using System.Windows.Controls;

namespace SolidRecords.App.Presentation;

public static class ActivityDatePicker
{
    public static readonly DependencyProperty AvailableDatesProperty = DependencyProperty.RegisterAttached(
        "AvailableDates", typeof(IReadOnlyList<DateTime>), typeof(ActivityDatePicker),
        new PropertyMetadata(null, AvailableDatesChanged));

    public static IReadOnlyList<DateTime>? GetAvailableDates(DependencyObject target) =>
        (IReadOnlyList<DateTime>?)target.GetValue(AvailableDatesProperty);

    public static void SetAvailableDates(DependencyObject target, IReadOnlyList<DateTime>? value) =>
        target.SetValue(AvailableDatesProperty, value);

    private static void AvailableDatesChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not DatePicker picker || e.NewValue is not IReadOnlyList<DateTime> dates) return;
        picker.BlackoutDates.Clear();
        var available = dates.Select(date => date.Date).Distinct().Order().ToArray();
        if (picker.SelectedDate is { } selected && !available.Contains(selected.Date))
            picker.SetCurrentValue(DatePicker.SelectedDateProperty, available.Contains(DateTime.Today)
                ? DateTime.Today : available.Select(date => (DateTime?)date).FirstOrDefault());
        var start = DateTime.MinValue.Date;
        foreach (var date in available)
        {
            if (start < date) picker.BlackoutDates.Add(new CalendarDateRange(start, date.AddDays(-1)));
            if (date == DateTime.MaxValue.Date) return;
            start = date.AddDays(1);
        }
        picker.BlackoutDates.Add(new CalendarDateRange(start, DateTime.MaxValue.Date));
    }
}

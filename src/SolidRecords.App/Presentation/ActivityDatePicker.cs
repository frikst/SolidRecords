using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace SolidRecords.App.Presentation;

public static class ActivityDatePicker
{
    public static readonly DependencyProperty FormattingLanguageProperty = DependencyProperty.RegisterAttached(
        "FormattingLanguage", typeof(XmlLanguage), typeof(ActivityDatePicker),
        new PropertyMetadata(null, FormattingLanguageChanged));

    public static XmlLanguage? GetFormattingLanguage(DependencyObject target) =>
        (XmlLanguage?)target.GetValue(FormattingLanguageProperty);

    public static void SetFormattingLanguage(DependencyObject target, XmlLanguage? value) =>
        target.SetValue(FormattingLanguageProperty, value);

    private static void FormattingLanguageChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not DatePicker picker || e.NewValue is not XmlLanguage language) return;
        // WPF changes date parsing on Language updates but leaves the existing text unchanged.
        // Set the parsing culture first, then format the selected value without changing selection.
        picker.SetCurrentValue(FrameworkElement.LanguageProperty, language);
        if (picker.SelectedDate is { } date)
            picker.SetCurrentValue(DatePicker.TextProperty, date.ToString(
                picker.SelectedDateFormat == DatePickerFormat.Short ? "d" : "D", language.GetSpecificCulture()));
    }

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

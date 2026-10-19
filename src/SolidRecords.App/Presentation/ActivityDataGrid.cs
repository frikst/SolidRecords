using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SolidRecords.App.Presentation;

public sealed class ActivityDataGrid : DataGrid
{
    private static readonly DependencyPropertyKey SessionContentOffsetPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SessionContentOffset), typeof(double), typeof(ActivityDataGrid), new PropertyMetadata(0.0));
    public static readonly DependencyProperty SessionContentOffsetProperty = SessionContentOffsetPropertyKey.DependencyProperty;
    public double SessionContentOffset => (double)GetValue(SessionContentOffsetProperty);

    public ActivityDataGrid()
    {
        Sorting += HandleSorting;
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.OriginalSource is ScrollViewer viewer && viewer.TemplatedParent == this)
                SetValue(SessionContentOffsetPropertyKey, -e.HorizontalOffset);
        }));
    }

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        if (Items is ICollectionViewLiveShaping { CanChangeLiveSorting: true } view)
            view.IsLiveSorting = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) ||
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        var direction = e.Key == Key.Down ? 1 : -1;
        var index = SelectedItem is null ? direction > 0 ? -1 : Items.Count : Items.IndexOf(SelectedItem);
        for (index += direction; index >= 0 && index < Items.Count; index += direction)
        {
            if (Items[index] is not ActivityRow activity) continue;
            SelectedItem = activity;
            var column = CurrentColumn is { Visibility: Visibility.Visible } current ? current
                : Columns.Where(value => value.Visibility == Visibility.Visible).MinBy(value => value.DisplayIndex);
            if (column is not null)
            {
                CurrentCell = new DataGridCellInfo(activity, column);
                ScrollIntoView(activity, column);
                UpdateLayout();
                DependencyObject? element = column.GetCellContent(activity);
                while (element is not null && element is not DataGridCell) element = VisualTreeHelper.GetParent(element);
                if (element is DataGridCell cell) cell.Focus();
            }
            break;
        }
        // At either boundary keep the selection and focus on the current activity.
        e.Handled = true;
    }

    private void HandleSorting(object sender, DataGridSortingEventArgs eventArgs)
    {
        eventArgs.Handled = true;

        var column = eventArgs.Column;
        ListSortDirection? direction = column.SortDirection switch
        {
            null => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => null
        };

        using (Items.DeferRefresh())
        {
            Items.SortDescriptions.Clear();
            foreach (var item in Columns) item.SortDirection = null;
            if (direction is { } value)
                Items.SortDescriptions.Add(new SortDescription(column.SortMemberPath, value));
            column.SortDirection = direction;
        }
    }
}
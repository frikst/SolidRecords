using System.Collections;
using System.ComponentModel;
using System.Windows.Controls;

namespace SolidRecords.App.Presentation;

public sealed class ActivityDataGrid : DataGrid
{
    public ActivityDataGrid() => Sorting += HandleSorting;

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        if (Items is ICollectionViewLiveShaping { CanChangeLiveSorting: true } view)
            view.IsLiveSorting = true;
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
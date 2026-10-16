using System.ComponentModel;
using System.Windows;
using SolidRecords.App.Presentation;

namespace SolidRecords.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        // WPF uses logical units; fit the initial size to the available desktop at its DPI scale.
        var workArea = SystemParameters.WorkArea;
        MinWidth = System.Math.Min(MinWidth, workArea.Width);
        MinHeight = System.Math.Min(MinHeight, workArea.Height);
        Width = System.Math.Min(Width, workArea.Width);
        Height = System.Math.Min(Height, workArea.Height);
        DataContext = viewModel;
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}

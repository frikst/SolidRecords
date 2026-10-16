using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SolidRecords.App.Presentation;
using SolidRecords.Windows.Shell;

namespace SolidRecords.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        AppLogo.Source = (ImageSource)Application.LoadComponent(
            new System.Uri("/SolidRecords.App;component/Assets/SolidRecords.xaml", System.UriKind.Relative));
        // WPF uses logical units; fit the initial size to the available desktop at its DPI scale.
        var workArea = SystemParameters.WorkArea;
        MinWidth = System.Math.Min(MinWidth, workArea.Width);
        MinHeight = System.Math.Min(MinHeight, workArea.Height);
        Width = System.Math.Min(Width, workArea.Width);
        Height = System.Math.Min(Height, workArea.Height);
        DataContext = viewModel;
    }

    public bool AllowClose { get; set; }

    protected override void OnSourceInitialized(System.EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowAppearance.ApplyRoundedCorners(new WindowInteropHelper(this).Handle);
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void ToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}

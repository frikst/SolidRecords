using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using SolidRecords.App.Presentation;
using SolidRecords.Windows.Shell;

namespace SolidRecords.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _viewModel.Saved += Saved;
        Closed += (_, _) => _viewModel.Saved -= Saved;
        MaxHeight = SystemParameters.WorkArea.Height;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
    }

    private void Saved(object? sender, EventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsSaving) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowAppearance.ApplyRoundedCorners(new WindowInteropHelper(this).Handle);
    }
}

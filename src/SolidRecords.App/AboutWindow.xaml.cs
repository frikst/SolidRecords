using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SolidRecords.App.Presentation;
using SolidRecords.Windows.Shell;

namespace SolidRecords.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(SolidRecords.Core.Localization.Strings.Culture.IetfLanguageTag);
        DataContext = new AboutViewModel();
        AppLogo.Source = (ImageSource)Application.LoadComponent(
            new System.Uri("/SolidRecords.App;component/Assets/SolidRecords.xaml", System.UriKind.Relative));
        MaxHeight = SystemParameters.WorkArea.Height;
        Width = System.Math.Min(Width, SystemParameters.WorkArea.Width);
    }

    protected override void OnSourceInitialized(System.EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowAppearance.ApplyRoundedCorners(new WindowInteropHelper(this).Handle);
    }
}

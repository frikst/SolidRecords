using SolidRecords.App.Presentation;
using SolidRecords.Core.Recording;

namespace SolidRecords.App.Tray;

internal sealed class TrayIconFactory(MainViewModel viewModel, Recorder recorder)
{
    public TrayIcon Create(Action showWindow, Action showAbout, Action showSettings, Func<Task> exit) =>
        new(viewModel, recorder, showWindow, showAbout, showSettings, exit);
}

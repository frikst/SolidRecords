using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Core.Recording;

namespace SolidRecords.App.Tray;

internal sealed class TrayIconFactory(MainViewModel viewModel, Recorder recorder)
{
    public TrayIcon Create(Action showWindow, Action showAbout, Func<Task> exit) =>
        new(viewModel, recorder, showWindow, showAbout, exit);
}

using SolidRecords.App.Presentation;
using SolidRecords.Core;

namespace SolidRecords.App;

internal sealed class TrayIconFactory(MainViewModel viewModel, Recorder recorder)
{
    public TrayIcon Create(Action showWindow, Action showAbout, Func<Task> exit) =>
        new(viewModel, recorder, showWindow, showAbout, exit);
}

using Microsoft.Toolkit.Uwp.Notifications;
using SolidRecords.Core.Updates;

namespace SolidRecords.Windows.Shell;

/// <summary>Actionable Windows notifications for unpackaged desktop applications.</summary>
public sealed class UpdateNotifications : IUpdateNotifier, IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<(string Action, string Tag)> _pending = new();
    private Action<string, string>? _handler;

    public UpdateNotifications() => ToastNotificationManagerCompat.OnActivated += OnActivated;

    public static bool WasToastActivated => Environment.GetCommandLineArgs().Contains("-ToastActivated", StringComparer.OrdinalIgnoreCase);

    public void SetActivationHandler(Action<string, string> handler)
    {
        lock (_sync)
        {
            _handler = handler;
            while (_pending.TryDequeue(out var item)) handler(item.Action, item.Tag);
        }
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var values = ToastArguments.Parse(args.Argument);
        if (!values.TryGetValue("action", out var action) || !values.TryGetValue("tag", out var tag)) return;
        lock (_sync)
        {
            if (_handler is null) _pending.Enqueue((action, tag));
            else _handler(action, tag);
        }
    }

    public static ToastContentBuilder CreateContent(UpdateRelease release) => new ToastContentBuilder()
        .AddArgument("action", "later").AddArgument("tag", release.Tag)
        .AddText($"SolidRecords {release.Tag[1..]} je k dispozícii")
        .AddText("Stiahnuť MSI a nainštalovať na pozadí? Windows požiada o oprávnenie správcu. Aplikácia sa pri inštalácii ukončí.")
        .AddButton(new ToastButton().SetContent("Stiahnuť a inštalovať").AddArgument("action", "install").AddArgument("tag", release.Tag))
        .AddButton(new ToastButton().SetContent("O 4 hodiny").AddArgument("action", "later").AddArgument("tag", release.Tag))
        .AddButton(new ToastButton().SetContent("Ignorovať verziu").AddArgument("action", "ignore").AddArgument("tag", release.Tag));

    public void Show(UpdateRelease release) => CreateContent(release)
        .Show(toast => { toast.Tag = "update"; toast.Group = "updates"; toast.ExpirationTime = DateTimeOffset.Now.AddDays(7); });

    public void Clear() => ToastNotificationManagerCompat.History.Remove("update", "updates");

    public void Dispose()
    {
        ToastNotificationManagerCompat.OnActivated -= OnActivated;
        lock (_sync) { _handler = null; _pending.Clear(); }
    }
}

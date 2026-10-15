using System.Windows.Automation;

namespace SolidRecords.Windows;

/// <summary>
/// UI Automation providers belong to other processes and can stall. All calls run
/// on one dedicated background MTA thread, never on the WPF or capture message loop.
/// </summary>
internal sealed class BrowserUrlReader : IDisposable
{
    private sealed record Request(nint Handle, Action<string?> Complete);
    private readonly AutoResetEvent _wake = new(false);
    private volatile Request? _request;
    private volatile bool _disposed;
    private readonly Thread _thread;

    public BrowserUrlReader()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Browser URL automation" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void RequestUrl(nint handle, Action<string?> complete)
    {
        _request = new(handle, complete);
        _wake.Set();
    }

    private void Run()
    {
        while (!_disposed)
        {
            _wake.WaitOne();
            if (_disposed) break;
            var request = Interlocked.Exchange(ref _request, null);
            if (request is null) continue;
            string? url = null;
            try { url = ReadAddressBar(request.Handle); }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException
                or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
            if (!_disposed) request.Complete(url);
        }
        _wake.Dispose();
    }

    private static string? ReadAddressBar(nint handle)
    {
        var root = AutomationElement.FromHandle(handle);
        var walker = TreeWalker.ControlViewWalker;
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;
        while (pending.TryPop(out var item) && visited++ < 350)
        {
            var current = item.Element.Current;
            // Never inspect the page's form inputs or document contents.
            if (current.ControlType == ControlType.Document) continue;
            if (current.ControlType == ControlType.Edit && IsAddressBar(current.AutomationId, current.Name))
            {
                // Do not record partially typed addresses or search queries.
                if (current.HasKeyboardFocus) return null;
                if (item.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                    return NormalizeUrl(((ValuePattern)pattern).Current.Value);
            }
            if (item.Depth >= 9) continue;
            var child = walker.GetFirstChild(item.Element);
            var siblings = 0;
            while (child is not null && siblings++ < 80)
            {
                pending.Push((child, item.Depth + 1));
                child = walker.GetNextSibling(child);
            }
        }
        return null;
    }

    private static bool IsAddressBar(string id, string name) =>
        id is "addressEditBox" or "urlbar-input" or "omnibox" ||
        name.Contains("address and search", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("search or enter address", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("adres", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Adress", StringComparison.OrdinalIgnoreCase);

    internal static string? NormalizeUrl(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Any(char.IsWhiteSpace)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "https" or "http" or "file" or "about" or "chrome" or "edge") return value;
        if (!value.Contains("://", StringComparison.Ordinal) && value.Contains('.') &&
            Uri.TryCreate("https://" + value, UriKind.Absolute, out var inferred) && inferred.Host.Contains('.'))
            return inferred.AbsoluteUri;
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        // A broken external accessibility provider must not hold application exit.
        _thread.Join(TimeSpan.FromSeconds(1));
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SolidRecords.Core;

namespace SolidRecords.Windows;

/// <summary>Used on the capture thread, including when the GUI is hidden.</summary>
internal sealed class ApplicationIconReader
{
    private readonly Dictionary<string, ApplicationIcon?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _cache.Clear();

    public ApplicationIcon? Read(WindowInfo window)
    {
        if (_cache.TryGetValue(window.Application, out var cached)) return cached;
        ApplicationIcon? icon = null;
        try
        {
            // WM_GETICON is bounded: an unresponsive application cannot hang capture.
            SendMessageTimeout((nint)window.Handle, 0x007F, 1, 0, 0x0001 | 0x0002 | 0x0020, 100, out var handle);
            if (handle != 0) icon = Encode(window.Application, handle);
            if (icon is null)
            {
                using var process = Process.GetProcessById(window.ProcessId);
                var file = process.MainModule?.FileName;
                if (file is not null && SHGetFileInfo(file, 0, out var info,
                    (uint)Marshal.SizeOf<ShellFileInfo>(), 0x00000100) != 0 && info.Icon != 0)
                {
                    try { icon = Encode(window.Application, info.Icon); }
                    finally { DestroyIcon(info.Icon); }
                }
            }
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException or IOException or NotSupportedException or COMException)
        {
            // Missing/inaccessible icons must never stop recording.
        }
        _cache[window.Application] = icon;
        return icon;
    }

    private static ApplicationIcon Encode(string application, nint handle)
    {
        // The WM_GETICON handle is borrowed; only shell-owned handles are destroyed above.
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new(application, Convert.ToBase64String(stream.ToArray()));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nint result);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}

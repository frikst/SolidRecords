using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SolidRecords.App.Tray;

/// <summary>Runs an owner-drawn HMENU in the real Windows menu loop used by the notification area.</summary>
internal sealed class NativeTrayMenu : NativeWindow, IDisposable
{
    private readonly TrayMenu _view;

    public NativeTrayMenu(TrayMenu view)
    {
        _view = view;
        CreateHandle(new CreateParams
        {
            Caption = "SolidRecords tray menu owner",
            Style = unchecked((int)0x80000000), // WS_POPUP: a top-level owner, not a message-only window.
            ExStyle = 0x80 // WS_EX_TOOLWINDOW
        });
    }

    internal int Show(Point anchor)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint background = 0;
        try
        {
            var color = _view.BackColor;
            background = CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16));
            if (background == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new MenuInfo { Size = (uint)Marshal.SizeOf<MenuInfo>(), Mask = 2, Background = background };
            if (!SetMenuInfo(menu, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            for (var index = 0; index < _view.Items.Count; index++)
            {
                var item = _view.Items[index];
                if (!item.Available) continue;
                var separator = item is ToolStripSeparator;
                var entry = new MenuItemInfo
                {
                    Size = (uint)Marshal.SizeOf<MenuItemInfo>(),
                    Mask = 0x143, // MIIM_FTYPE | MIIM_STRING | MIIM_ID | MIIM_STATE
                    Type = 0x100u | (separator ? 0x800u : 0), // MFT_OWNERDRAW | MFT_SEPARATOR
                    Id = (uint)index + 1,
                    State = !item.Enabled ? 3u : item is ToolStripMenuItem { Checked: true } ? 8u : 0,
                    Text = item.Text ?? ""
                };
                if (!InsertMenuItem(menu, uint.MaxValue, true, ref entry))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            // A real TrackPopupMenuEx loop exposes GUI_INMENUMODE to the shell. A ToolStrip popup does not.
            SetForegroundWindow(Handle);
            return (int)TrackPopupMenuEx(menu, 0x12A, anchor.X, anchor.Y, Handle, 0);
            // TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_RIGHTALIGN | TPM_BOTTOMALIGN
        }
        finally
        {
            DestroyMenu(menu);
            if (background != 0) DeleteObject(background);
            // Required by the notification-icon menu protocol before the next activation.
            PostMessage(Handle, 0, 0, 0); // WM_NULL
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x002C) // WM_MEASUREITEM
        {
            var measure = Marshal.PtrToStructure<MeasureItem>(message.LParam);
            if (measure.Type == 1 && measure.Id > 0 && measure.Id <= _view.Items.Count) // ODT_MENU
            {
                var item = _view.Items[(int)measure.Id - 1];
                measure.Width = (uint)item.Width;
                measure.Height = (uint)item.Height;
                Marshal.StructureToPtr(measure, message.LParam, false);
                message.Result = 1;
                return;
            }
        }
        else if (message.Msg == 0x002B) // WM_DRAWITEM
        {
            var draw = Marshal.PtrToStructure<DrawItem>(message.LParam);
            if (draw.Type == 1 && draw.Id > 0 && draw.Id <= _view.Items.Count)
            {
                using var graphics = Graphics.FromHdc(draw.DeviceContext);
                _view.DrawNativeItem(graphics, (int)draw.Id - 1,
                    Rectangle.FromLTRB(draw.Bounds.Left, draw.Bounds.Top, draw.Bounds.Right, draw.Bounds.Bottom),
                    (draw.State & 1) != 0); // ODS_SELECTED
                message.Result = 1;
                return;
            }
        }
        base.WndProc(ref message);
    }

    public void Dispose() => DestroyHandle();

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuInfo
    {
        public uint Size, Mask, Style, MaxHeight;
        public nint Background;
        public uint ContextHelpId;
        public nuint Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint Size, Mask, Type, State, Id;
        public nint SubMenu, CheckedBitmap, UncheckedBitmap;
        public nuint Data;
        [MarshalAs(UnmanagedType.LPWStr)] public string Text;
        public uint TextLength;
        public nint Bitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeasureItem
    {
        public uint Type, ControlId, Id, Width, Height;
        public nuint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawItem
    {
        public uint Type, ControlId, Id, Action, State;
        public nint ItemWindow, DeviceContext;
        public NativeRect Bounds;
        public nuint Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuInfo(nint menu, ref MenuInfo info);
    [DllImport("user32.dll", EntryPoint = "InsertMenuItemW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenuItem(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo info);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}

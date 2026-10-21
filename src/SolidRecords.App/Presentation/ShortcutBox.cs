using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SolidRecords.Core;
using SolidRecords.Core.Localization;

namespace SolidRecords.App.Presentation;

public sealed class ShortcutBox : TextBox
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(nameof(Shortcut),
        typeof(GlobalShortcut), typeof(ShortcutBox), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ShortcutBox)d).UpdateText()));

    public GlobalShortcut? Shortcut
    {
        get => (GlobalShortcut?)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public ShortcutBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        UpdateText();
    }

    private void UpdateText()
    {
        if (Shortcut is not { } shortcut) { Text = Strings.Get("ShortcutUnset"); return; }
        var parts = new List<string>();
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
        var key = shortcut.VirtualKey;
        parts.Add(key >= 0x70 ? $"F{key - 0x6F}" : ((char)key).ToString());
        Text = string.Join(" + ", parts);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        if (e.IsRepeat) return;
        if (key is Key.Delete or Key.Back) { SetCurrentValue(ShortcutProperty, null); return; }
        var modifiers = ShortcutModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= ShortcutModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= ShortcutModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= ShortcutModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= ShortcutModifiers.Windows;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if ((modifiers & ~ShortcutModifiers.Shift) != 0 && GlobalShortcut.IsSupportedKey(virtualKey))
            SetCurrentValue(ShortcutProperty, new GlobalShortcut(modifiers, virtualKey));
    }
}

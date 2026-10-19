namespace SolidRecords.Core;

[Flags]
public enum ShortcutModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public sealed record GlobalShortcut(ShortcutModifiers Modifiers, int VirtualKey)
{
    public static bool IsSupportedKey(int key) => key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87;

    public void Validate()
    {
        if ((Modifiers & ~(ShortcutModifiers.Alt | ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Windows)) != 0
            || (Modifiers & ~ShortcutModifiers.Shift) == 0 || !IsSupportedKey(VirtualKey))
            throw new ArgumentException("Skratka musí obsahovať Ctrl, Alt alebo Win a písmeno, číslicu alebo F1–F24.");
    }
}

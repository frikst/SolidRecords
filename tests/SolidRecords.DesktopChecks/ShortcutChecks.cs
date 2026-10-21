using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SolidRecords.App.Infrastructure;
using SolidRecords.App.Presentation;
using SolidRecords.Core;
using SolidRecords.Windows.Shell;

namespace SolidRecords.DesktopChecks;

internal static class ShortcutChecks
{
    public static async Task RunAsync(SolidRecords.App.App app, IServiceProvider provider)
    {
        var preferences = provider.GetRequiredService<PreferencesStore>();
        var previous = preferences.Current;
        var first = new GlobalShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift, 0x86);
        var second = first with { VirtualKey = 0x87 };
        var configured = previous with { ToggleWindowShortcut = first, ToggleRecordingShortcut = second };
        await app.SaveSettingsAsync(configured);
        Require(await provider.GetRequiredService<ISettingsStore>().ReadSettingsAsync() == configured, "Shortcuts did not persist.");
        app.ShowWindow();
        var dialogCheck = new TaskCompletionSource();
        _ = app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            SolidRecords.App.SettingsWindow? dialog = null;
            try
            {
                dialog = System.Windows.Application.Current.Windows.OfType<SolidRecords.App.SettingsWindow>().Single();
                var tabs = (System.Windows.Controls.TabControl)dialog.FindName("SettingsTabs");
                tabs.SelectedIndex = 4;
                dialog.UpdateLayout();
                var field = (ShortcutBox)dialog.FindName("WindowShortcut");
                Require(field.Focus(), "Shortcut field did not accept focus.");
                Press(second);
                await Task.Delay(150);
                Require(field.Shortcut == second, $"Shortcut field did not capture an already registered combination. Active: {dialog.IsActive}, keyboard focus: {field.IsKeyboardFocusWithin}, captured: {field.Shortcut}, expected: {second}.");
                Require(app.MainWindow!.IsVisible, "Editing shortcuts triggered a global action.");
                var editor = (SettingsViewModel)dialog.DataContext;
                editor.SaveCommand.Execute(null);
                Require(editor.Error.Length > 0 && !editor.IsSaving, "Duplicate shortcuts were accepted.");
                KeyEvent(0x2E, 0, 0, 0);
                KeyEvent(0x2E, 0, 2, 0);
                await Task.Delay(100);
                Require(field.Shortcut is null && editor.ToggleWindowShortcut is null, "Delete did not clear the shortcut binding.");
                dialogCheck.SetResult();
            }
            catch (Exception ex) { dialogCheck.SetException(ex); }
            finally { dialog?.Close(); }
        }));
        app.ShowSettingsWindow();
        await dialogCheck.Task;
        Press(first);
        await Task.Delay(100);
        Require(!app.MainWindow!.IsVisible, "Global shortcut did not hide the window.");
        Press(first);
        await Task.Delay(100);
        Require(app.MainWindow.IsVisible, "Global shortcut did not reopen the window.");
        app.MainWindow.Hide();
        var viewModel = provider.GetRequiredService<MainViewModel>();
        var before = viewModel.Status;
        Press(second);
        await Task.Delay(250);
        Require(viewModel.Status != before && !app.MainWindow.IsVisible, "Recording shortcut did not work with hidden UI.");
        Press(second);
        await Task.Delay(250);
        Require(viewModel.Status == before, "Recording shortcut did not restore recording.");
        Require(preferences.RecordOnLaunch == previous.RecordOnLaunch, "Shortcut changed launch preference.");

        var swapped = configured with { ToggleWindowShortcut = second, ToggleRecordingShortcut = first };
        await app.SaveSettingsAsync(swapped);
        Press(second);
        await Task.Delay(100);
        Require(app.MainWindow.IsVisible, "Swapping shortcuts did not update actions.");

        using var competitor = new GlobalShortcuts();
        var saved = false;
        try
        {
            await competitor.ApplyAsync(configured, () => { }, () => { }, () => { saved = true; return Task.CompletedTask; });
            throw new Exception("Conflicting registration succeeded.");
        }
        catch (InvalidOperationException) { Require(!saved, "Conflict was persisted."); }

        await app.SaveSettingsAsync(previous);
        var noAction = new Action(() => { });
        await competitor.ApplyAsync(configured, noAction, noAction, () => Task.CompletedTask);
        await competitor.ApplyAsync(previous, noAction, noAction, () => Task.CompletedTask);
        using var owner = new GlobalShortcuts();
        await owner.ApplyAsync(configured, noAction, noAction, () => Task.CompletedTask);
        try
        {
            await competitor.ApplyAsync(previous with { ToggleWindowShortcut = first with { VirtualKey = 0x85 } },
                noAction, noAction, () => Task.FromException(new IOException("Expected save failure")));
        }
        catch (IOException) { }
        using var afterFailure = new GlobalShortcuts();
        await afterFailure.ApplyAsync(previous with { ToggleWindowShortcut = first with { VirtualKey = 0x85 } },
            noAction, noAction, () => Task.CompletedTask);
        owner.Dispose();
        await competitor.ApplyAsync(configured, noAction, noAction, () => Task.CompletedTask);
        competitor.Dispose();
        Console.WriteLine("PASS: global shortcuts, hidden UI, recording, persistence, swaps, conflicts, rollback and disposal.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Press(GlobalShortcut shortcut)
    {
        // Only synthetic test combinations; always release modifiers.
        try
        {
            KeyEvent(0x11, 0, 0, 0);
            KeyEvent(0x12, 0, 0, 0);
            KeyEvent(0x10, 0, 0, 0);
            KeyEvent((byte)shortcut.VirtualKey, 0, 0, 0);
            KeyEvent((byte)shortcut.VirtualKey, 0, 2, 0);
        }
        finally
        {
            KeyEvent(0x10, 0, 2, 0);
            KeyEvent(0x12, 0, 2, 0);
            KeyEvent(0x11, 0, 2, 0);
        }
    }

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeyEvent(byte key, byte scan, uint flags, nuint extra);
}

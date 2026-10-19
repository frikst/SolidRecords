using System.Text.Json;
using SolidRecords.Core;
using Xunit;

namespace SolidRecords.Tests;

public sealed class GlobalShortcutTests
{
    [Fact]
    public void DefaultsAndLegacySettingsDoNotEnableShortcuts()
    {
        foreach (var settings in new[] { new ApplicationSettings(), JsonSerializer.Deserialize<ApplicationSettings>("{\"RecordOnLaunch\":false}")! })
        {
            Assert.Null(settings.ToggleWindowShortcut);
            Assert.Null(settings.ToggleRecordingShortcut);
        }
    }

    [Fact]
    public void ShortcutsRoundTripAndDuplicatesAreRejected()
    {
        var shortcut = new GlobalShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x52);
        var settings = new ApplicationSettings(ToggleWindowShortcut: shortcut,
            ToggleRecordingShortcut: new(ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x70));
        settings.Validate();
        Assert.Equal(settings, JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings)));
        Assert.Throws<ArgumentException>(() => (settings with { ToggleRecordingShortcut = shortcut }).Validate());
    }

    [Theory]
    [InlineData(0, 0x52)]
    [InlineData(4, 0x52)]
    [InlineData(16, 0x52)]
    [InlineData(2, 0)]
    [InlineData(2, 0x10)]
    [InlineData(2, 0x88)]
    public void InvalidCombinationsAreRejected(int modifiers, int key) =>
        Assert.Throws<ArgumentException>(() => new GlobalShortcut((ShortcutModifiers)modifiers, key).Validate());
}

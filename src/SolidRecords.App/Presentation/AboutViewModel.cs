using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;

namespace SolidRecords.App.Presentation;

public sealed class AboutViewModel : ObservableObject
{
    private string _websiteError = "";

    public AboutViewModel() => OpenWebsiteCommand = new RelayCommand(OpenWebsite);

    public string Version
    {
        get
        {
            var version = typeof(AboutViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(version) ? "Vývojová zostava" : $"Verzia {version.Split('+')[0]}";
        }
    }
    public ICommand OpenWebsiteCommand { get; }
    public string WebsiteError
    {
        get => _websiteError;
        private set => Set(ref _websiteError, value);
    }

    private void OpenWebsite()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("https://www.fri.uniza.sk/pracoviska/KST")
            {
                UseShellExecute = true
            });
            WebsiteError = "";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            WebsiteError = "Web sa nepodarilo otvoriť. Adresa: https://www.fri.uniza.sk/pracoviska/KST";
        }
    }
}

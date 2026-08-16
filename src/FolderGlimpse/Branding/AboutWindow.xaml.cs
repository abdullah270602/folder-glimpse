using System.Reflection;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using FolderGlimpse.Core.Application;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Updates;

namespace FolderGlimpse.Branding;

public partial class AboutView : System.Windows.Controls.UserControl
{
    private readonly IUpdateChecker _updates;
    private readonly ISettingsService _settings;
    private Uri? _releasePage;
    private readonly string _version;

    internal AboutView(IUpdateChecker updates, ISettingsService settings)
    {
        _updates = updates;
        _settings = settings;
        InitializeComponent();
        _version = (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "Unknown").Split('+')[0];
        VersionText.Text = $"Version {_version}";
    }

    private async void CheckForUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        ViewReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Checking GitHub Releases…";
        try
        {
            var result = await _updates.CheckAsync();
            _releasePage = result.UpdateAvailable ? result.ReleasePage : null;
            UpdateStatusText.Text = result.Message;
            ViewReleaseButton.Visibility = _releasePage is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _releasePage = null;
            UpdateStatusText.Text = "Could not check for updates. Check your connection and try again.";
        }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private void ViewRelease(object sender, RoutedEventArgs e)
    {
        if (_releasePage is not { } page || !string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    private void CopyDiagnostics(object sender, RoutedEventArgs e)
    {
        var report = DiagnosticReport.Create(_settings.Current, _version, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.FrameworkDescription);
        System.Windows.Clipboard.SetText(report);
        UpdateStatusText.Text = "Diagnostics copied. No file or folder paths were included.";
    }
}

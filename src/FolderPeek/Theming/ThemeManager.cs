using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using FolderPeek.Core.Settings;
using MediaColor = System.Windows.Media.Color;

namespace FolderPeek.Theming;

internal sealed class ThemeManager : IDisposable
{
    private ThemePreference _preference;
    internal bool IsDark { get; private set; }
    internal event EventHandler? Changed;

    internal ThemeManager(ThemePreference preference)
    {
        _preference = preference;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Refresh();
    }

    internal void SetPreference(ThemePreference preference) { _preference = preference; Refresh(); }

    internal void Apply(FrameworkElement target)
    {
        var dark = IsDark;
        target.Resources["PanelBrush"] = Brush(dark ? MediaColor.FromRgb(35, 35, 35) : MediaColor.FromRgb(247, 247, 247));
        target.Resources["TextBrush"] = Brush(dark ? MediaColor.FromRgb(244, 244, 244) : MediaColor.FromRgb(23, 23, 23));
        target.Resources["SubtleTextBrush"] = Brush(dark ? MediaColor.FromRgb(181, 181, 181) : MediaColor.FromRgb(102, 102, 102));
        target.Resources["LineBrush"] = Brush(dark ? MediaColor.FromArgb(32, 255, 255, 255) : MediaColor.FromArgb(24, 0, 0, 0));
        target.Resources["ControlBrush"] = Brush(dark ? MediaColor.FromRgb(48, 48, 48) : MediaColor.FromRgb(255, 255, 255));
        target.Resources["ScrollTrackBrush"] = Brush(dark ? MediaColor.FromArgb(22, 255, 255, 255) : MediaColor.FromArgb(18, 0, 0, 0));
        target.Resources["ScrollThumbBrush"] = Brush(dark ? MediaColor.FromArgb(112, 255, 255, 255) : MediaColor.FromArgb(92, 0, 0, 0));
        target.Resources["ScrollThumbHoverBrush"] = Brush(dark ? MediaColor.FromArgb(168, 255, 255, 255) : MediaColor.FromArgb(132, 0, 0, 0));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var next = _preference switch { ThemePreference.Dark => true, ThemePreference.Light => false, _ => SystemUsesDark() };
        if (next == IsDark) return;
        IsDark = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch { return false; }
    }

    private static SolidColorBrush Brush(System.Windows.Media.Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}

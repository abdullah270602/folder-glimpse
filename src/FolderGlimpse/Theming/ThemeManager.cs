using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using FolderGlimpse.Core.Settings;
using MediaColor = System.Windows.Media.Color;

namespace FolderGlimpse.Theming;

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
        target.Resources["WindowBrush"] = Brush(dark ? MediaColor.FromRgb(28, 28, 28) : MediaColor.FromRgb(243, 243, 243));
        target.Resources["PanelBrush"] = Brush(dark ? MediaColor.FromRgb(32, 32, 32) : MediaColor.FromRgb(249, 249, 249));
        target.Resources["ControlBrush"] = Brush(dark ? MediaColor.FromRgb(43, 43, 43) : MediaColor.FromRgb(255, 255, 255));
        target.Resources["ControlHoverBrush"] = Brush(dark ? MediaColor.FromRgb(52, 52, 52) : MediaColor.FromRgb(247, 247, 247));
        target.Resources["ControlPressedBrush"] = Brush(dark ? MediaColor.FromRgb(60, 60, 60) : MediaColor.FromRgb(238, 238, 238));
        target.Resources["TextBrush"] = Brush(dark ? MediaColor.FromRgb(255, 255, 255) : MediaColor.FromRgb(26, 26, 26));
        target.Resources["SubtleTextBrush"] = Brush(dark ? MediaColor.FromRgb(190, 190, 190) : MediaColor.FromRgb(97, 97, 97));
        target.Resources["DisabledTextBrush"] = Brush(dark ? MediaColor.FromRgb(126, 126, 126) : MediaColor.FromRgb(154, 154, 154));
        target.Resources["LineBrush"] = Brush(dark ? MediaColor.FromArgb(34, 255, 255, 255) : MediaColor.FromArgb(22, 0, 0, 0));
        target.Resources["AccentBrush"] = Brush(dark ? MediaColor.FromRgb(96, 205, 255) : MediaColor.FromRgb(0, 103, 192));
        target.Resources["AccentHoverBrush"] = Brush(dark ? MediaColor.FromRgb(126, 214, 255) : MediaColor.FromRgb(25, 117, 197));
        target.Resources["AccentPressedBrush"] = Brush(dark ? MediaColor.FromRgb(71, 173, 221) : MediaColor.FromRgb(0, 90, 158));
        target.Resources["AccentTextBrush"] = Brush(dark ? MediaColor.FromRgb(0, 35, 52) : MediaColor.FromRgb(255, 255, 255));
        target.Resources["BrandBlueBrush"] = Brush(dark ? MediaColor.FromRgb(96, 165, 250) : MediaColor.FromRgb(37, 99, 235));
        target.Resources["ScrollTrackBrush"] = Brush(dark ? MediaColor.FromArgb(18, 255, 255, 255) : MediaColor.FromArgb(12, 0, 0, 0));
        target.Resources["ScrollThumbBrush"] = Brush(dark ? MediaColor.FromArgb(105, 255, 255, 255) : MediaColor.FromArgb(82, 0, 0, 0));
        target.Resources["ScrollThumbHoverBrush"] = Brush(dark ? MediaColor.FromArgb(170, 255, 255, 255) : MediaColor.FromArgb(145, 0, 0, 0));
        target.Resources["SelectionBrush"] = Brush(dark ? MediaColor.FromArgb(58, 96, 205, 255) : MediaColor.FromArgb(38, 0, 103, 192));
        target.Resources["SelectionBorderBrush"] = Brush(dark ? MediaColor.FromRgb(96, 205, 255) : MediaColor.FromRgb(0, 103, 192));
    }

    internal void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var enabled = IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
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

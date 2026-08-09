using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FolderPeek.Core;

namespace FolderPeek.Preview;

public partial class PreviewWindow : Window
{
    internal PreviewViewModel ViewModel { get; } = new();
    private nint _handle;

    internal PreviewWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        SourceInitialized += OnSourceInitialized;
        Closing += PreventClose;
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new nint(style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
        var preference = NativeMethods.DwmwcpRoundSmall;
        NativeMethods.DwmSetWindowAttribute(_handle, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private void PreventClose(object? sender, CancelEventArgs args)
    {
        if (System.Windows.Application.Current?.Dispatcher.HasShutdownStarted == false) { args.Cancel = true; Hide(); }
    }

    internal void ApplySystemTheme()
    {
        var light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) != 0;
        }
        catch { }
        Resources["PanelBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromRgb(247, 247, 247) : System.Windows.Media.Color.FromRgb(35, 35, 35));
        Resources["TextBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromRgb(23, 23, 23) : System.Windows.Media.Color.FromRgb(244, 244, 244));
        Resources["SubtleTextBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromRgb(102, 102, 102) : System.Windows.Media.Color.FromRgb(181, 181, 181));
        Resources["LineBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromArgb(24, 0, 0, 0) : System.Windows.Media.Color.FromArgb(32, 255, 255, 255));
        Resources["ScrollTrackBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromArgb(18, 0, 0, 0) : System.Windows.Media.Color.FromArgb(22, 255, 255, 255));
        Resources["ScrollThumbBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromArgb(92, 0, 0, 0) : System.Windows.Media.Color.FromArgb(112, 255, 255, 255));
        Resources["ScrollThumbHoverBrush"] = new SolidColorBrush(light ? System.Windows.Media.Color.FromArgb(132, 0, 0, 0) : System.Windows.Media.Color.FromArgb(168, 255, 255, 255));
        Background = (System.Windows.Media.Brush)Resources["PanelBrush"];
    }

    internal void ShowBeside(PixelRect anchor, double widthDip, double maxHeightDip, int visibleRows, double rowHeightDip)
    {
        ApplySystemTheme();
        Width = widthDip;
        MaxHeight = maxHeightDip;
        EntryList.Tag = rowHeightDip;
        EntryList.MaxHeight = (Math.Max(1, visibleRows) * rowHeightDip) + EntryList.Padding.Top + EntryList.Padding.Bottom;
        SizeToContent = SizeToContent.Height;
        Show();
        UpdateLayout();
        SizeToContent = SizeToContent.Manual;
        Height = Math.Min(Math.Max(ActualHeight, MinHeight), MaxHeight);
        var nativeAnchor = new NativeMethods.Rect { Left = anchor.Left, Top = anchor.Top, Right = anchor.Right, Bottom = anchor.Bottom };
        var monitor = NativeMethods.MonitorFromRect(ref nativeAnchor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var dpi = NativeMethods.GetDpiForWindow(_handle);
        if (dpi == 0) dpi = 96;
        var popupSize = new PixelSize((int)Math.Ceiling(Width * dpi / 96d), (int)Math.Ceiling(Height * dpi / 96d));
        var work = new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        var placed = PopupPositioner.Place(anchor, work, popupSize);
        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, placed.Left, placed.Top, placed.Width, placed.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }
}

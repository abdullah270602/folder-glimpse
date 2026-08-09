using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FolderPeek.Core;
using FolderPeek.Core.Settings;
using FolderPeek.Theming;

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

    internal void ApplyTheme(ThemeManager theme) { theme.Apply(this); Background = (System.Windows.Media.Brush)Resources["PanelBrush"]; }

    internal void ShowBeside(PixelRect anchor, FolderPeekSettings settings)
    {
        Width = settings.PopupWidth;
        MaxHeight = settings.MaxPopupHeight;
        EntryList.Tag = settings.PreviewRowHeightDip;
        EntryList.MaxHeight = (settings.PreviewVisibleRows * settings.PreviewRowHeightDip) + EntryList.Padding.Top + EntryList.Padding.Bottom;
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

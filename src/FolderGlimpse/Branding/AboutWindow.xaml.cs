using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderGlimpse.Theming;

namespace FolderGlimpse.Branding;

public partial class AboutWindow : Window
{
    private readonly ThemeManager _theme;

    internal AboutWindow(ThemeManager theme)
    {
        _theme = theme;
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version}";
        SourceInitialized += (_, _) => _theme.ApplyWindowChrome(this);
        _theme.Apply(this);
    }

    internal void RefreshTheme()
    {
        _theme.Apply(this);
        if (IsLoaded) _theme.ApplyWindowChrome(this);
    }

    internal void CaptureTo(string path)
    {
        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void DoneClicked(object sender, RoutedEventArgs e) => Close();
}

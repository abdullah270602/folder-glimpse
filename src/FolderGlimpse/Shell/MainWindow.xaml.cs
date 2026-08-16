using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderGlimpse.Branding;
using FolderGlimpse.Core.Application;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Settings;
using FolderGlimpse.Startup;
using FolderGlimpse.Theming;
using FolderGlimpse.Updates;

namespace FolderGlimpse.Shell;

public partial class MainWindow : Window, IDisposable
{
    private readonly IApplicationStateService _appState;
    private readonly IStartupRegistration _startup;
    private readonly ThemeManager _theme;
    private readonly MainViewModel _viewModel;
    private readonly HomeView _home;
    private readonly SettingsView _settings;
    private readonly HowToUseView _howToUse;
    private readonly AboutView _about;
    private readonly WelcomeView _welcome;
    private readonly ShellNavigationModel _navigation = new();
    private bool _allowClose;
    private bool _disposed;

    internal MainWindow(ISettingsService settings, IStartupRegistration startup, IApplicationStateService appState,
        ThemeManager theme, IUpdateChecker updates, Func<bool> getEnabled, Action<bool> setEnabled)
    {
        _appState = appState;
        _startup = startup;
        _theme = theme;
        _viewModel = new MainViewModel(settings, startup, getEnabled, setEnabled);
        InitializeComponent();
        DataContext = _viewModel;
        _home = new HomeView(_viewModel);
        _settings = new SettingsView(settings, startup);
        _howToUse = new HowToUseView(_viewModel);
        _about = new AboutView(updates, settings);
        _welcome = new WelcomeView(startup.IsEnabled);
        _settings.HomeRequested += () => Navigate(ShellSection.Home);
        _welcome.GetStartedRequested += CompleteOnboarding;
        Closing += WindowClosing;
        SourceInitialized += (_, _) => _theme.ApplyWindowChrome(this);
        RefreshTheme();
    }

    internal ShellSection CurrentSection => _navigation.Current;
    internal bool ShowingWelcome => WelcomeHost.Visibility == Visibility.Visible;

    internal void ShowSurface(InitialSurface surface, bool activate = true)
    {
        if (surface == InitialSurface.None) return;
        if (surface == InitialSurface.Welcome) ShowWelcome();
        else Navigate(surface switch
        {
            InitialSurface.Settings => ShellSection.Settings,
            InitialSurface.About => ShellSection.About,
            _ => ShellSection.Home
        });
        ShowAndActivate(activate);
    }

    internal void HandleActivation(ActivationRequest request)
    {
        var surface = request switch
        {
            ActivationRequest.Settings => InitialSurface.Settings,
            ActivationRequest.About => InitialSurface.About,
            _ => InitialSurfacePolicy.Decide(new(LaunchIntentKind.Normal), _appState.Current.HasCompletedOnboarding)
        };
        ShowSurface(surface);
    }

    internal void Navigate(ShellSection section)
    {
        WelcomeHost.Visibility = Visibility.Collapsed;
        ShellGrid.Visibility = Visibility.Visible;
        _navigation.Navigate(section);
        HomeNav.IsChecked = section == ShellSection.Home;
        SettingsNav.IsChecked = section == ShellSection.Settings;
        HowToNav.IsChecked = section == ShellSection.HowToUse;
        AboutNav.IsChecked = section == ShellSection.About;
        PageHost.Content = section switch
        {
            ShellSection.Settings => _settings,
            ShellSection.HowToUse => _howToUse,
            ShellSection.About => _about,
            _ => _home
        };
        _viewModel.Refresh();
    }

    internal void RefreshState() => _viewModel.Refresh();
    internal void RefreshTheme() { _theme.Apply(this); if (IsLoaded) _theme.ApplyWindowChrome(this); }

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
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(stream);
    }

    internal void CaptureSettingsTo(string path, bool scrollToEnd, bool showInteraction)
    {
        Navigate(ShellSection.Settings);
        UpdateLayout();
        _settings.CaptureTo(path, scrollToEnd, showInteraction);
    }

    internal void PrepareForExit()
    {
        _allowClose = true;
        Close();
    }

    private void ShowWelcome()
    {
        ShellGrid.Visibility = Visibility.Collapsed;
        WelcomeHost.Visibility = Visibility.Visible;
        WelcomeHost.Content = _welcome;
    }

    private void CompleteOnboarding(bool launchAtStartup)
    {
        if (launchAtStartup != _startup.IsEnabled && !_startup.TrySetEnabled(launchAtStartup, out var startupError))
        {
            _welcome.ShowError(startupError ?? "Windows could not update the startup preference.");
            return;
        }
        if (!_appState.TryUpdate(state => state with { HasCompletedOnboarding = true }, out var stateError))
        {
            _welcome.ShowError(stateError ?? "FolderGlimpse could not save the welcome state.");
            return;
        }
        _welcome.ShowError(string.Empty);
        Navigate(ShellSection.Home);
    }

    private void ShowAndActivate(bool activate)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!activate) return;
        Activate();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) NativeMethods.SetForegroundWindow(handle);
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void HomeClicked(object sender, RoutedEventArgs e) => Navigate(ShellSection.Home);
    private void SettingsClicked(object sender, RoutedEventArgs e) => Navigate(ShellSection.Settings);
    private void HowToClicked(object sender, RoutedEventArgs e) => Navigate(ShellSection.HowToUse);
    private void AboutClicked(object sender, RoutedEventArgs e) => Navigate(ShellSection.About);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Dispose();
        _viewModel.Dispose();
    }
}

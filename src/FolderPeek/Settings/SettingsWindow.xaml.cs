using System.Windows;
using FolderPeek.Core.Settings;
using FolderPeek.Startup;
using FolderPeek.Theming;

namespace FolderPeek.Settings;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly ThemeManager _theme;
    private bool _loading;

    internal SettingsWindow(ISettingsService settings, IStartupRegistration startup, ThemeManager theme)
    {
        _settings = settings; _startup = startup; _theme = theme;
        InitializeComponent();
        ThemeBox.ItemsSource = Enum.GetValues<ThemePreference>();
        DensityBox.ItemsSource = Enum.GetValues<DisplayDensity>();
        SortBox.ItemsSource = Enum.GetValues<SortMode>();
        HotkeyBox.ItemsSource = Enum.GetValues<TriggerHotkey>();
        TapBox.ItemsSource = Enum.GetValues<TapBehavior>();
        LimitBox.ItemsSource = new[] { new Choice("20", 20), new("50", 50), new("100", 100), new("200", 200), new("All", 0) };
        Loaded += (_, _) => LoadValues();
        _settings.SettingsChanged += SettingsChanged;
        _startup.Changed += StartupStateChanged;
        Closed += (_, _) => { _settings.SettingsChanged -= SettingsChanged; _startup.Changed -= StartupStateChanged; };
        _theme.Apply(this);
    }

    internal void RefreshTheme() => _theme.Apply(this);
    private void SettingsChanged(object? sender, SettingsChangedEventArgs e) => Dispatcher.BeginInvoke(LoadValues);
    private void StartupStateChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(LoadValues);

    private void LoadValues()
    {
        _loading = true;
        var s = _settings.Current;
        ThemeBox.SelectedItem = s.Theme; WidthSlider.Value = s.PopupWidth; HeightSlider.Value = s.MaxPopupHeight;
        PathCheck.IsChecked = s.ShowFullPath; SizeCheck.IsChecked = s.ShowFileSize; DateCheck.IsChecked = s.ShowModifiedDate;
        HiddenCheck.IsChecked = s.ShowHiddenFiles; SortBox.SelectedItem = s.SortMode; FoldersCheck.IsChecked = s.FoldersFirst;
        LimitBox.SelectedItem = LimitBox.Items.Cast<Choice>().First(x => x.Value == s.InitialItemLimit);
        DensityBox.SelectedItem = s.Density; HotkeyBox.SelectedItem = s.Hotkey; HoldSlider.Value = s.HoldThresholdMs;
        TapBox.SelectedItem = s.TapBehavior; StartupCheck.IsChecked = _startup.IsEnabled;
        UpdateLabels(); ErrorText.Text = _settings.LastError ?? string.Empty;
        _loading = false;
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        UpdateLabels();
        var limit = (LimitBox.SelectedItem as Choice)?.Value ?? 50;
        _settings.TryUpdate(s => s with
        {
            Theme = ThemeBox.SelectedItem is ThemePreference theme ? theme : s.Theme,
            PopupWidth = WidthSlider.Value, MaxPopupHeight = HeightSlider.Value,
            ShowFullPath = PathCheck.IsChecked == true, ShowFileSize = SizeCheck.IsChecked == true,
            ShowModifiedDate = DateCheck.IsChecked == true, ShowHiddenFiles = HiddenCheck.IsChecked == true,
            SortMode = SortBox.SelectedItem is SortMode sort ? sort : s.SortMode,
            FoldersFirst = FoldersCheck.IsChecked == true, InitialItemLimit = limit,
            Density = DensityBox.SelectedItem is DisplayDensity density ? density : s.Density,
            Hotkey = HotkeyBox.SelectedItem is TriggerHotkey hotkey ? hotkey : s.Hotkey,
            HoldThresholdMs = (int)HoldSlider.Value,
            TapBehavior = TapBox.SelectedItem is TapBehavior tap ? tap : s.TapBehavior
        }, out var error);
        ErrorText.Text = error ?? string.Empty;
    }

    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        if (!_startup.TrySetEnabled(StartupCheck.IsChecked == true, out var error))
        {
            _loading = true; StartupCheck.IsChecked = _startup.IsEnabled; _loading = false;
        }
        ErrorText.Text = error ?? string.Empty;
    }

    private void UpdateLabels()
    {
        WidthLabel.Text = $"Popup width  ·  {(int)WidthSlider.Value} px";
        HeightLabel.Text = $"Maximum height  ·  {(int)HeightSlider.Value} px";
        HoldLabel.Text = $"Hold delay  ·  {(int)HoldSlider.Value} ms";
    }

    private void ResetClicked(object sender, RoutedEventArgs e) { _settings.TryResetDefaults(out var error); ErrorText.Text = error ?? string.Empty; LoadValues(); }
    private void DoneClicked(object sender, RoutedEventArgs e) => Close();
    private sealed record Choice(string Label, int Value) { public override string ToString() => Label; }
}

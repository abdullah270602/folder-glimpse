using System.Windows;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Startup;
using FolderGlimpse.Theming;

namespace FolderGlimpse.Settings;

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
        ThemeBox.ItemsSource = new[] { new Choice<ThemePreference>("Use Windows setting", ThemePreference.System), new("Light", ThemePreference.Light), new("Dark", ThemePreference.Dark) };
        DensityBox.ItemsSource = new[] { new Choice<DisplayDensity>("Comfortable", DisplayDensity.Comfortable), new("Compact", DisplayDensity.Compact) };
        SortBox.ItemsSource = new[] { new Choice<SortMode>("Name", SortMode.Name), new("Modified date", SortMode.ModifiedDate), new("File type", SortMode.Type) };
        HotkeyBox.ItemsSource = new[] { new Choice<TriggerHotkey>("Space", TriggerHotkey.Space), new("Ctrl + Space", TriggerHotkey.ControlSpace) };
        TapBox.ItemsSource = new[] { new Choice<TapBehavior>("Toggle preview", TapBehavior.TogglePreview), new("Momentary only", TapBehavior.MomentaryOnly) };
        LimitBox.ItemsSource = new[] { new Choice<int>("20 items", 20), new("50 items", 50), new("100 items", 100), new("200 items", 200), new("All items", 0) };
        Loaded += (_, _) => LoadValues();
        _settings.SettingsChanged += SettingsChanged;
        _startup.Changed += StartupStateChanged;
        Closed += (_, _) => { _settings.SettingsChanged -= SettingsChanged; _startup.Changed -= StartupStateChanged; };
        SourceInitialized += (_, _) => _theme.ApplyWindowChrome(this);
        _theme.Apply(this);
    }

    internal void RefreshTheme() { _theme.Apply(this); if (IsLoaded) _theme.ApplyWindowChrome(this); }

    internal void CaptureTo(string path, bool scrollToEnd = false, bool showInteraction = false)
    {
        UpdateLayout();
        if (showInteraction)
        {
            var position = InteractionHeading.TranslatePoint(new System.Windows.Point(0, 0), MainScrollViewer);
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + position.Y - 8);
        }
        else if (scrollToEnd) MainScrollViewer.ScrollToEnd();
        else MainScrollViewer.ScrollToTop();
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
    private void SettingsChanged(object? sender, SettingsChangedEventArgs e) => Dispatcher.BeginInvoke(LoadValues);
    private void StartupStateChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(LoadValues);

    private void LoadValues()
    {
        _loading = true;
        var s = _settings.Current;
        ThemeBox.SelectedItem = FindChoice(ThemeBox, s.Theme); WidthSlider.Value = s.PopupWidth; HeightSlider.Value = s.MaxPopupHeight;
        PathCheck.IsChecked = s.ShowFullPath; SizeCheck.IsChecked = s.ShowFileSize; DateCheck.IsChecked = s.ShowModifiedDate;
        HiddenCheck.IsChecked = s.ShowHiddenFiles; SortBox.SelectedItem = FindChoice(SortBox, s.SortMode); FoldersCheck.IsChecked = s.FoldersFirst;
        LimitBox.SelectedItem = FindChoice(LimitBox, s.InitialItemLimit);
        DensityBox.SelectedItem = FindChoice(DensityBox, s.Density); HotkeyBox.SelectedItem = FindChoice(HotkeyBox, s.Hotkey); HoldSlider.Value = s.HoldThresholdMs;
        TapBox.SelectedItem = FindChoice(TapBox, s.TapBehavior); StartupCheck.IsChecked = _startup.IsEnabled;
        InteractiveCheck.IsChecked = s.InteractiveItems; DoubleFileCheck.IsChecked = s.DoubleClickFilesToOpen;
        DoubleFolderCheck.IsChecked = s.DoubleClickFoldersToOpen; RightClickCheck.IsChecked = s.RightClickActions;
        MultiCheck.IsChecked = s.MultiSelection; SelectionCheckboxCheck.IsChecked = s.ShowSelectionCheckboxes;
        AllowMultiOpenCheck.IsChecked = s.AllowOpeningMultipleItems; ConfirmSlider.Value = s.ConfirmBeforeOpeningMoreThan;
        CloseAfterOpenCheck.IsChecked = s.ClosePreviewAfterOpening;
        UpdateLabels(); ErrorText.Text = _settings.LastError ?? string.Empty;
        UpdateDependencies();
        _loading = false;
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        UpdateLabels();
        var limit = (LimitBox.SelectedItem as Choice<int>)?.Value ?? 50;
        _settings.TryUpdate(s => s with
        {
            Theme = (ThemeBox.SelectedItem as Choice<ThemePreference>)?.Value ?? s.Theme,
            PopupWidth = WidthSlider.Value, MaxPopupHeight = HeightSlider.Value,
            ShowFullPath = PathCheck.IsChecked == true, ShowFileSize = SizeCheck.IsChecked == true,
            ShowModifiedDate = DateCheck.IsChecked == true, ShowHiddenFiles = HiddenCheck.IsChecked == true,
            SortMode = (SortBox.SelectedItem as Choice<SortMode>)?.Value ?? s.SortMode,
            FoldersFirst = FoldersCheck.IsChecked == true, InitialItemLimit = limit,
            Density = (DensityBox.SelectedItem as Choice<DisplayDensity>)?.Value ?? s.Density,
            Hotkey = (HotkeyBox.SelectedItem as Choice<TriggerHotkey>)?.Value ?? s.Hotkey,
            HoldThresholdMs = (int)HoldSlider.Value,
            TapBehavior = (TapBox.SelectedItem as Choice<TapBehavior>)?.Value ?? s.TapBehavior,
            InteractiveItems = InteractiveCheck.IsChecked == true,
            DoubleClickFilesToOpen = DoubleFileCheck.IsChecked == true,
            DoubleClickFoldersToOpen = DoubleFolderCheck.IsChecked == true,
            RightClickActions = RightClickCheck.IsChecked == true,
            MultiSelection = MultiCheck.IsChecked == true,
            ShowSelectionCheckboxes = SelectionCheckboxCheck.IsChecked == true,
            AllowOpeningMultipleItems = AllowMultiOpenCheck.IsChecked == true,
            ConfirmBeforeOpeningMoreThan = (int)ConfirmSlider.Value,
            ClosePreviewAfterOpening = CloseAfterOpenCheck.IsChecked == true
        }, out var error);
        ErrorText.Text = error ?? string.Empty;
        UpdateDependencies();
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
        WidthLabel.Text = $"{(int)WidthSlider.Value} px";
        HeightLabel.Text = $"{(int)HeightSlider.Value} px";
        HoldLabel.Text = $"{(int)HoldSlider.Value} ms";
        ConfirmLabel.Text = $"{(int)ConfirmSlider.Value} items";
    }

    private void UpdateDependencies()
    {
        var interactive = InteractiveCheck.IsChecked == true;
        DoubleFileCard.IsEnabled = interactive; DoubleFolderCard.IsEnabled = interactive; RightClickCard.IsEnabled = interactive;
        MultiCard.IsEnabled = interactive; CloseAfterCard.IsEnabled = interactive;
        var multi = interactive && MultiCheck.IsChecked == true;
        CheckboxCard.IsEnabled = multi; AllowMultiCard.IsEnabled = multi;
        ConfirmCard.IsEnabled = multi && AllowMultiOpenCheck.IsChecked == true;
    }

    private void ResetClicked(object sender, RoutedEventArgs e) { _settings.TryResetDefaults(out var error); ErrorText.Text = error ?? string.Empty; LoadValues(); }
    private void DoneClicked(object sender, RoutedEventArgs e) => Close();
    private static object? FindChoice<T>(System.Windows.Controls.ComboBox box, T value) => box.Items.Cast<Choice<T>>().FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }
}

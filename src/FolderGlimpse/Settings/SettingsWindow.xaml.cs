using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Startup;

namespace FolderGlimpse.Settings;

public partial class SettingsView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private bool _loading;
    private bool _disposed;
    internal event Action? HomeRequested;

    internal SettingsView(ISettingsService settings, IStartupRegistration startup)
    {
        _settings = settings; _startup = startup;
        InitializeComponent();
        ThemeBox.ItemsSource = new[] { new Choice<ThemePreference>("Use Windows setting", ThemePreference.System), new("Light", ThemePreference.Light), new("Dark", ThemePreference.Dark) };
        DensityBox.ItemsSource = new[] { new Choice<DisplayDensity>("Comfortable", DisplayDensity.Comfortable), new("Compact", DisplayDensity.Compact) };
        SortBox.ItemsSource = new[] { new Choice<SortMode>("Name", SortMode.Name), new("Modified date", SortMode.ModifiedDate), new("File type", SortMode.Type) };
        HotkeyBox.ItemsSource = new[] { new Choice<TriggerHotkey>("Space", TriggerHotkey.Space), new("Ctrl + Space", TriggerHotkey.ControlSpace) };
        TapBox.ItemsSource = new[] { new Choice<TapBehavior>("Toggle preview", TapBehavior.TogglePreview), new("Momentary only", TapBehavior.MomentaryOnly) };
        HoverModeBox.ItemsSource = new[] { new Choice<HoverPreviewMode>("Off", HoverPreviewMode.Off), new("Selected folder", HoverPreviewMode.SelectedFolder), new("Any folder", HoverPreviewMode.AnyFolder) };
        HoverModifierBox.ItemsSource = new[] { new Choice<HoverModifier>("None", HoverModifier.None), new("Ctrl", HoverModifier.Control), new("Shift", HoverModifier.Shift) };
        LimitBox.ItemsSource = new[] { new Choice<int>("20 items", 20), new("50 items", 50), new("100 items", 100), new("200 items", 200), new("All items", 0) };
        Loaded += (_, _) => LoadValues();
        _settings.SettingsChanged += SettingsChanged;
        _startup.Changed += StartupStateChanged;
    }

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
        HoverModeBox.SelectedItem = FindChoice(HoverModeBox, s.HoverMode);
        HoverModifierBox.SelectedItem = FindChoice(HoverModifierBox, s.HoverModifier);
        HoverOpenSlider.Value = s.HoverOpenDelayMs; HoverCloseSlider.Value = s.HoverCloseDelayMs;
        HoverToleranceSlider.Value = s.HoverMovementTolerancePx;
        MouseMiddleCheck.IsChecked = s.MouseTriggers.HasFlag(MouseTriggerOptions.MiddleClick);
        MouseCtrlLeftCheck.IsChecked = s.MouseTriggers.HasFlag(MouseTriggerOptions.ControlLeftClick);
        MouseCtrlRightCheck.IsChecked = s.MouseTriggers.HasFlag(MouseTriggerOptions.ControlRightClick);
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
        var mouseTriggers = (MouseMiddleCheck.IsChecked == true ? MouseTriggerOptions.MiddleClick : MouseTriggerOptions.None) |
            (MouseCtrlLeftCheck.IsChecked == true ? MouseTriggerOptions.ControlLeftClick : MouseTriggerOptions.None) |
            (MouseCtrlRightCheck.IsChecked == true ? MouseTriggerOptions.ControlRightClick : MouseTriggerOptions.None);
        var saved = _settings.TryUpdate(s => s with
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
            HoverMode = (HoverModeBox.SelectedItem as Choice<HoverPreviewMode>)?.Value ?? s.HoverMode,
            HoverModifier = (HoverModifierBox.SelectedItem as Choice<HoverModifier>)?.Value ?? s.HoverModifier,
            HoverOpenDelayMs = (int)HoverOpenSlider.Value,
            HoverCloseDelayMs = (int)HoverCloseSlider.Value,
            HoverMovementTolerancePx = (int)HoverToleranceSlider.Value,
            MouseTriggers = mouseTriggers,
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
        if (!saved) LoadValues();
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
        HoverOpenLabel.Text = $"{(int)HoverOpenSlider.Value} ms";
        HoverCloseLabel.Text = $"{(int)HoverCloseSlider.Value} ms";
        HoverToleranceLabel.Text = $"{(int)HoverToleranceSlider.Value} px";
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
        var hover = (HoverModeBox.SelectedItem as Choice<HoverPreviewMode>)?.Value is not HoverPreviewMode.Off;
        HoverModifierCard.IsEnabled = hover; HoverOpenCard.IsEnabled = hover;
        HoverCloseCard.IsEnabled = hover; HoverToleranceCard.IsEnabled = hover;
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // WPF's default wheel routing can jump several card rows at once. Keep navigation
        // deliberate and predictable while still respecting high-resolution wheel deltas.
        MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + SettingsScrollPolicy.OffsetDelta(e.Delta));
        e.Handled = true;
    }

    private void ResetClicked(object sender, RoutedEventArgs e) { _settings.TryResetDefaults(out var error); ErrorText.Text = error ?? string.Empty; LoadValues(); }
    private void DoneClicked(object sender, RoutedEventArgs e) => HomeRequested?.Invoke();
    private static object? FindChoice<T>(System.Windows.Controls.ComboBox box, T value) => box.Items.Cast<Choice<T>>().FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.SettingsChanged -= SettingsChanged;
        _startup.Changed -= StartupStateChanged;
    }
}

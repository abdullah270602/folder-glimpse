using System.ComponentModel;
using System.Runtime.CompilerServices;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Startup;

namespace FolderGlimpse.Shell;

internal sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly Func<bool> _getEnabled;
    private readonly Action<bool> _setEnabled;

    internal MainViewModel(ISettingsService settings, IStartupRegistration startup, Func<bool> getEnabled, Action<bool> setEnabled)
    {
        _settings = settings;
        _startup = startup;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;
        _settings.SettingsChanged += SettingsChanged;
        _startup.Changed += StartupChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEnabled
    {
        get => _getEnabled();
        set { if (value == _getEnabled()) return; _setEnabled(value); Refresh(); }
    }

    public string StatusText => IsEnabled ? "FolderGlimpse is active" : "FolderGlimpse is paused";
    public string StatusDetail => IsEnabled ? "Ready in File Explorer" : "Preview shortcuts are temporarily disabled";
    public string ShortcutText => _settings.Current.Hotkey == TriggerHotkey.ControlSpace ? "Ctrl + Space" : "Space";
    public string TapTitle => _settings.Current.TapBehavior == TapBehavior.MomentaryOnly ? "Tap disabled" : $"Tap {ShortcutText}";
    public string TapDescription => _settings.Current.TapBehavior == TapBehavior.MomentaryOnly ? "Hold the shortcut to preview" : "Keep a glimpse open";
    public string HoldTitle => $"Hold {ShortcutText}";

    internal void Refresh()
    {
        void Raise()
        {
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusDetail));
            OnPropertyChanged(nameof(ShortcutText));
            OnPropertyChanged(nameof(TapTitle));
            OnPropertyChanged(nameof(TapDescription));
            OnPropertyChanged(nameof(HoldTitle));
        }
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(Raise); else Raise();
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs e) => Refresh();
    private void StartupChanged(object? sender, EventArgs e) => Refresh();
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { _settings.SettingsChanged -= SettingsChanged; _startup.Changed -= StartupChanged; }
}

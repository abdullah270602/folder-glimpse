using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using FolderGlimpse.Core.FolderInspection;
using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Preview;

public sealed class PreviewViewModel : INotifyPropertyChanged
{
    private string _folderName = string.Empty;
    private string _folderPath = string.Empty;
    private string _status = string.Empty;
    private bool _loading;
    private bool _showPath = true;
    private int _selectedCount;
    private bool _showCheckboxes;
    private bool _showEntryIcons = true;
    private string _errorMessage = string.Empty;
    private bool _isTruncated;
    private PopupFooterStyle _footerStyle = PopupFooterStyle.Always;

    public ObservableCollection<PreviewEntryViewModel> Entries { get; } = new();
    public string FolderName { get => _folderName; set => Set(ref _folderName, value); }
    public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool ShowPath { get => _showPath; set { if (Set(ref _showPath, value)) Changed(nameof(PathVisibility)); } }
    public Visibility PathVisibility => ShowPath ? Visibility.Visible : Visibility.Collapsed;
    public bool Loading { get => _loading; set { if (Set(ref _loading, value)) { Changed(nameof(LoadingVisibility)); Changed(nameof(EmptyVisibility)); } } }
    public Visibility LoadingVisibility => Loading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => !Loading && string.IsNullOrWhiteSpace(ErrorMessage) && Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public int SelectedCount { get => _selectedCount; set { if (Set(ref _selectedCount, value)) { Changed(nameof(SelectionStatus)); Changed(nameof(ActionBarVisibility)); } } }
    public string SelectionStatus => $"{SelectedCount} selected";
    public Visibility ActionBarVisibility => SelectedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    public bool ShowCheckboxes { get => _showCheckboxes; set => Set(ref _showCheckboxes, value); }
    public bool ShowEntryIcons { get => _showEntryIcons; set => Set(ref _showEntryIcons, value); }
    public string ErrorMessage { get => _errorMessage; set { if (Set(ref _errorMessage, value)) { Changed(nameof(ErrorVisibility)); Changed(nameof(EmptyVisibility)); } } }
    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsTruncated { get => _isTruncated; set { if (Set(ref _isTruncated, value)) Changed(nameof(FooterVisibility)); } }
    public PopupFooterStyle FooterStyle { get => _footerStyle; set { if (Set(ref _footerStyle, value)) Changed(nameof(FooterVisibility)); } }
    public Visibility FooterVisibility => FooterStyle == PopupFooterStyle.Always ||
        (FooterStyle == PopupFooterStyle.Smart && IsTruncated) ? Visibility.Visible : Visibility.Collapsed;

    internal void EntriesChanged() => Changed(nameof(EmptyVisibility));
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PreviewEntryViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public PreviewEntryViewModel(FolderEntry entry, string sizeText, string modifiedText, bool showSize, bool showModified, BitmapSource? icon)
    {
        Entry = entry;
        SizeText = sizeText;
        ModifiedText = modifiedText;
        SizeVisibility = showSize && sizeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ModifiedVisibility = showModified ? Visibility.Visible : Visibility.Collapsed;
        Icon = icon;
    }

    public FolderEntry Entry { get; }
    public string Name => Entry.Name;
    public string SizeText { get; }
    public string ModifiedText { get; }
    public Visibility SizeVisibility { get; }
    public Visibility ModifiedVisibility { get; }
    public BitmapSource? Icon { get; }
    public bool IsSelected { get => _isSelected; internal set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

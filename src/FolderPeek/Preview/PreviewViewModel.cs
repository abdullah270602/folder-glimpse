using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FolderPeek.Preview;

public sealed class PreviewViewModel : INotifyPropertyChanged
{
    private string _folderName = string.Empty;
    private string _folderPath = string.Empty;
    private string _status = string.Empty;
    private bool _loading;
    private bool _showPath = true;

    public ObservableCollection<PreviewEntryViewModel> Entries { get; } = new();
    public string FolderName { get => _folderName; set => Set(ref _folderName, value); }
    public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool ShowPath { get => _showPath; set { if (Set(ref _showPath, value)) Changed(nameof(PathVisibility)); } }
    public Visibility PathVisibility => ShowPath ? Visibility.Visible : Visibility.Collapsed;
    public bool Loading { get => _loading; set { if (Set(ref _loading, value)) { Changed(nameof(LoadingVisibility)); Changed(nameof(EmptyVisibility)); } } }
    public Visibility LoadingVisibility => Loading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => !Loading && Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void EntriesChanged() => Changed(nameof(EmptyVisibility));
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PreviewEntryViewModel
{
    public PreviewEntryViewModel(string name, string sizeText, string modifiedText, bool showSize, bool showModified, BitmapSource? icon)
    {
        Name = name;
        SizeText = sizeText;
        ModifiedText = modifiedText;
        SizeVisibility = showSize && sizeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ModifiedVisibility = showModified ? Visibility.Visible : Visibility.Collapsed;
        Icon = icon;
    }

    public string Name { get; }
    public string SizeText { get; }
    public string ModifiedText { get; }
    public Visibility SizeVisibility { get; }
    public Visibility ModifiedVisibility { get; }
    public BitmapSource? Icon { get; }
}

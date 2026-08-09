namespace FolderGlimpse.Shell;

public partial class HomeView : System.Windows.Controls.UserControl
{
    internal event Action? SettingsRequested;
    internal HomeView(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
    private void SettingsClicked(object sender, System.Windows.RoutedEventArgs e) => SettingsRequested?.Invoke();
}

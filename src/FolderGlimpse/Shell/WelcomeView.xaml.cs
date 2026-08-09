namespace FolderGlimpse.Shell;

public partial class WelcomeView : System.Windows.Controls.UserControl
{
    internal event Action<bool>? GetStartedRequested;
    internal WelcomeView(bool launchAtStartup) { InitializeComponent(); StartupCheck.IsChecked = launchAtStartup; }
    internal void ShowError(string message) => ErrorText.Text = message;
    private void GetStartedClicked(object sender, System.Windows.RoutedEventArgs e) => GetStartedRequested?.Invoke(StartupCheck.IsChecked == true);
}

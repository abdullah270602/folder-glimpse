namespace FolderGlimpse.Shell;

public partial class HomeView : System.Windows.Controls.UserControl
{
    internal HomeView(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}

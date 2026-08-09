namespace FolderGlimpse.Shell;

public partial class HowToUseView : System.Windows.Controls.UserControl
{
    internal HowToUseView(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}

using System.Reflection;
using System.Windows;

namespace FolderGlimpse.Branding;

public partial class AboutView : System.Windows.Controls.UserControl
{
    internal AboutView()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "Unknown";
        VersionText.Text = $"Version {version.Split('+')[0]}";
    }
}

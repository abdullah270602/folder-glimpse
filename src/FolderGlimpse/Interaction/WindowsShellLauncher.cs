using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FolderGlimpse.Core.Interaction;
using FolderGlimpse.Theming;

namespace FolderGlimpse.Interaction;

internal sealed class WindowsShellLauncher : IShellLauncher
{
    public Task OpenFileAsync(string path, CancellationToken cancellationToken = default) => LaunchAsync(path, null, cancellationToken);
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) => LaunchAsync(path, null, cancellationToken);
    public Task OpenFileLocationAsync(string path, CancellationToken cancellationToken = default) =>
        LaunchAsync(Path.GetDirectoryName(path) ?? path, null, cancellationToken);
    public Task ShowPropertiesAsync(string path, CancellationToken cancellationToken = default) => LaunchAsync(path, "properties", cancellationToken);

    private static Task LaunchAsync(string path, string? verb, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The selected item no longer exists.", path);
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            if (verb is not null) info.Verb = verb;
            try { Process.Start(info); }
            catch (Win32Exception exception) { throw new InvalidOperationException("Windows could not open the selected item.", exception); }
        }, cancellationToken);
    }
}

internal sealed class WpfOpenManyConfirmation(System.Windows.Window owner, ThemeManager theme) : IOpenManyConfirmation
{
    public Task<bool> ConfirmAsync(int itemCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenManyDialog(itemCount, theme) { Owner = owner };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}

internal sealed class OpenManyDialog : System.Windows.Window
{
    internal OpenManyDialog(int itemCount, ThemeManager theme)
    {
        Title = $"Open {itemCount} items?";
        Width = 430; SizeToContent = System.Windows.SizeToContent.Height; ResizeMode = System.Windows.ResizeMode.NoResize;
        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");

        var root = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(24) };
        root.RowDefinitions.Add(new() { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = System.Windows.GridLength.Auto });
        var heading = new System.Windows.Controls.TextBlock { Text = $"Open {itemCount} items?", FontSize = 18, FontWeight = System.Windows.FontWeights.SemiBold };
        var message = new System.Windows.Controls.TextBlock { Text = "This may open multiple applications or File Explorer windows.", TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new(0, 10, 0, 22) };
        System.Windows.Controls.Grid.SetRow(message, 1);
        var actions = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 90, IsCancel = true, Margin = new(0, 0, 10, 0) };
        var open = new System.Windows.Controls.Button { Content = "Open All", MinWidth = 100, IsDefault = true, Style = (System.Windows.Style)System.Windows.Application.Current.Resources["PrimaryButtonStyle"] };
        open.Click += (_, _) => { DialogResult = true; Close(); };
        actions.Children.Add(cancel); actions.Children.Add(open); System.Windows.Controls.Grid.SetRow(actions, 2);
        root.Children.Add(heading); root.Children.Add(message); root.Children.Add(actions); Content = root;
        SourceInitialized += (_, _) => theme.ApplyWindowChrome(this);
        theme.Apply(this);
    }
}

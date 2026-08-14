using FolderGlimpse.Core.Interaction;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Preview;

namespace FolderGlimpse.UiTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var window = new PreviewWindow(new NoOpShellLauncher());
            var defaults = FolderGlimpseSettings.Default;

            window.ConfigureInteraction(PreviewInteractionMode.ViewOnly, defaults);
            False(window.EntryList.IsHitTestVisible, "view-only previews must ignore pointer input");
            False(window.EntryList.Focusable, "view-only previews must not take keyboard focus");

            window.ConfigureInteraction(PreviewInteractionMode.HoverPointer, defaults);
            True(window.EntryList.IsHitTestVisible, "hover previews must receive file and folder double-clicks");
            False(window.EntryList.Focusable, "hover previews must remain non-activating");
            False(window.ViewModel.ShowCheckboxes, "hover previews must not expose sticky selection controls");

            window.ConfigureInteraction(PreviewInteractionMode.Sticky, defaults);
            True(window.EntryList.IsHitTestVisible, "sticky previews must receive pointer input");
            True(window.EntryList.Focusable, "sticky previews must support keyboard navigation");

            window.ConfigureInteraction(PreviewInteractionMode.HoverPointer,
                defaults with { InteractiveItems = false });
            False(window.EntryList.IsHitTestVisible, "the interaction master switch must disable hover clicks");

            Console.WriteLine("4/4 WPF preview interaction checks passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL  WPF preview interaction: {exception.Message}");
            return 1;
        }
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private sealed class NoOpShellLauncher : IShellLauncher
    {
        public Task OpenFileAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenFileLocationAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowPropertiesAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

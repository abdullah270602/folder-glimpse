using FolderGlimpse.Core.Interaction;
using FolderGlimpse.Core.FolderInspection;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Preview;
using FolderGlimpse.Updates;
using FolderGlimpse.Input;
using System.Net;
using System.Net.Http;
using System.Text;

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
            True(window.HeaderPanel.Visibility == System.Windows.Visibility.Visible, "full header is visible by default");
            True(window.FolderPathText.Visibility == System.Windows.Visibility.Visible, "full header shows the configured path");
            True(window.FooterText.Visibility == System.Windows.Visibility.Visible, "always footer is visible by default");

            window.ConfigureInteraction(PreviewInteractionMode.ViewOnly, defaults with
            {
                HeaderStyle = PopupHeaderStyle.Compact,
                FooterStyle = PopupFooterStyle.Smart,
                ShowEntryIcons = false
            });
            True(window.HeaderPanel.Visibility == System.Windows.Visibility.Visible, "compact header retains folder identity");
            True(window.FolderPathText.Visibility == System.Windows.Visibility.Collapsed, "compact header removes the path");
            True(window.ViewModel.FooterVisibility == System.Windows.Visibility.Collapsed, "smart footer hides ordinary counts");
            False(window.ViewModel.ShowEntryIcons, "hidden icons propagate to the row presentation");
            window.ViewModel.IsTruncated = true;
            True(window.ViewModel.FooterVisibility == System.Windows.Visibility.Visible, "smart footer exposes truncation notices");

            window.ConfigureInteraction(PreviewInteractionMode.ViewOnly, defaults with
            {
                HeaderStyle = PopupHeaderStyle.Hidden,
                FooterStyle = PopupFooterStyle.Hidden
            });
            True(window.HeaderPanel.Visibility == System.Windows.Visibility.Collapsed &&
                window.HeaderDivider.Visibility == System.Windows.Visibility.Collapsed, "hidden header removes its divider and chrome");
            True(window.ViewModel.FooterVisibility == System.Windows.Visibility.Collapsed, "hidden footer removes its divider and chrome");
            window.ViewModel.ErrorMessage = "Access is unavailable.";
            True(window.ViewModel.ErrorVisibility == System.Windows.Visibility.Visible, "read errors stay visible without a footer");
            True(window.ViewModel.EmptyVisibility == System.Windows.Visibility.Collapsed, "an error never also claims the folder is empty");
            window.ViewModel.ErrorMessage = string.Empty;

            window.ConfigureInteraction(PreviewInteractionMode.HoverPointer, defaults);
            True(window.EntryList.IsHitTestVisible, "hover previews must receive file and folder double-clicks");
            False(window.EntryList.Focusable, "hover previews must remain non-activating");
            False(window.ViewModel.ShowCheckboxes, "hover previews must not expose sticky selection controls");
            True(window.EntryList.ToolTip is string hoverTip && hoverTip.Contains("pin", StringComparison.OrdinalIgnoreCase),
                "hover previews explain click-to-pin without adding permanent visual clutter");

            window.ViewModel.Entries.Add(new PreviewEntryViewModel(
                new FolderEntry("Folder", @"C:\Folder", true, null, DateTimeOffset.UnixEpoch),
                string.Empty, string.Empty, false, false, null));
            var promotionRaised = false;
            window.PromoteRequested += () =>
            {
                promotionRaised = true;
                window.ConfigureInteraction(PreviewInteractionMode.Sticky, defaults);
            };
            True(window.TryPromoteEntry(0), "a hover item click must promote the preview");
            True(promotionRaised, "hover promotion must be handed to the application state owner");
            True(window.EntryList.Focusable, "the promoted preview must become keyboard interactive");
            True(window.ViewModel.Entries[0].IsSelected && window.ViewModel.SelectedCount == 1,
                "the item clicked during promotion must become the sticky selection");

            window.ConfigureInteraction(PreviewInteractionMode.Sticky, defaults);
            True(window.EntryList.IsHitTestVisible, "sticky previews must receive pointer input");
            True(window.EntryList.Focusable, "sticky previews must support keyboard navigation");
            True(window.EntryList.ToolTip is null, "sticky previews remove hover-only promotion guidance");

            window.ConfigureInteraction(PreviewInteractionMode.HoverPointer,
                defaults with { InteractiveItems = false });
            False(window.EntryList.IsHitTestVisible, "the interaction master switch must disable hover clicks");

            const string releases = """
                [
                  { "tag_name": "v0.1.0-beta.2", "draft": false, "html_url": "https://github.com/abdullah270602/folder-glimpse/releases/tag/v0.1.0-beta.2" },
                  { "tag_name": "v0.1.0-beta.3", "draft": false, "html_url": "https://github.com/abdullah270602/folder-glimpse/releases/tag/v0.1.0-beta.3" },
                  { "tag_name": "v9.0.0", "draft": true, "html_url": "https://github.com/abdullah270602/folder-glimpse/releases/tag/v9.0.0" }
                ]
                """;
            var update = new GitHubUpdateChecker(new JsonHandler(releases), "0.1.0-beta.2")
                .CheckAsync().GetAwaiter().GetResult();
            True(update.UpdateAvailable && update.LatestVersion == "0.1.0-beta.3",
                "manual update checks include newer published beta releases and ignore drafts");
            True(update.ReleasePage?.Host == "github.com", "update links remain on the official GitHub host");
            var stableUpdate = new GitHubUpdateChecker(new JsonHandler(releases), "0.1.0")
                .CheckAsync().GetAwaiter().GetResult();
            False(stableUpdate.UpdateAvailable, "stable builds do not opt users into prerelease updates");

            using (var mouseHook = new MouseTriggerHook(_ => null))
            {
                mouseHook.Start(true);
                mouseHook.SetEnabled(false);
                mouseHook.SetEnabled(true);
            }

            Console.WriteLine("24/24 WPF, update, and native hook checks passed");
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

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            True(request.Headers.UserAgent.Count > 0, "GitHub requests include a User-Agent");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class NoOpShellLauncher : IShellLauncher
    {
        public Task OpenFileAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenFileLocationAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowPropertiesAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

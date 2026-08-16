using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using FolderGlimpse.Core.Application;

namespace FolderGlimpse.Updates;

internal sealed record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, string LatestVersion,
    Uri? ReleasePage, string Message);

internal interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

internal sealed class GitHubUpdateChecker : IUpdateChecker
{
    private static readonly Uri ReleasesApi = new("https://api.github.com/repos/abdullah270602/folder-glimpse/releases?per_page=10");
    private readonly HttpClient _client;
    private readonly string _currentVersion;

    internal GitHubUpdateChecker(HttpMessageHandler? handler = null, string? currentVersion = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = TimeSpan.FromSeconds(8);
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FolderGlimpse", "1"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _currentVersion = currentVersion ??
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ??
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!SemanticVersion.TryParse(_currentVersion, out var current))
            return new(false, _currentVersion, _currentVersion, null, "This build has an unknown version.");
        var includePrereleases = current.Prerelease is not null;
        using var response = await _client.GetAsync(ReleasesApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        SemanticVersion? latest = null;
        string? latestText = null;
        Uri? releasePage = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString();
            if (!SemanticVersion.TryParse(tag, out var candidate) ||
                (!includePrereleases && candidate.Prerelease is not null) ||
                (latest is not null && candidate.CompareTo(latest.Value) <= 0)) continue;
            if (!Uri.TryCreate(release.GetProperty("html_url").GetString(), UriKind.Absolute, out var page) ||
                !string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase)) continue;
            latest = candidate;
            latestText = tag?.TrimStart('v', 'V');
            releasePage = page;
        }
        if (latest is null || latestText is null)
            return new(false, _currentVersion, _currentVersion, null, "No published release was found.");
        var available = latest.Value.CompareTo(current) > 0;
        return new(available, _currentVersion, latestText, releasePage,
            available ? $"Version {latestText} is available." : "You have the latest available version.");
    }
}

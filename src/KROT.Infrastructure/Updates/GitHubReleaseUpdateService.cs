using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.Updates;
using Newtonsoft.Json;

namespace KROT.Infrastructure.Updates;

public sealed class GitHubReleaseUpdateService : IReleaseUpdateService
{
    private const int MaxDescriptionLength = 320;
    private static readonly Uri ReleasesEndpoint = new(
        "https://api.github.com/repos/morda-mir/KROT-zapret/releases?per_page=20");
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient _httpClient;
    private readonly Uri _releasesEndpoint;

    public GitHubReleaseUpdateService(
        HttpClient? httpClient = null,
        Uri? releasesEndpoint = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _releasesEndpoint = releasesEndpoint ?? ReleasesEndpoint;
    }

    public async Task<ReleaseUpdateInfo?> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var installedVersion)
            || installedVersion == null)
        {
            throw new InvalidOperationException(
                $"The installed version '{currentVersion}' is not a numeric release version.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _releasesEndpoint);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"KROT-zapret/{currentVersion}");

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(json)
            ?? new List<GitHubRelease>();

        GitHubRelease? selectedRelease = null;
        ReleaseVersion? selectedVersion = null;
        foreach (var release in releases.Where(item => !item.Draft && !item.PreRelease))
        {
            if (!ReleaseVersion.TryParse(release.TagName, out var releaseVersion)
                || releaseVersion == null
                || releaseVersion.CompareTo(installedVersion) <= 0
                || selectedVersion != null
                && releaseVersion.CompareTo(selectedVersion) <= 0
                || !IsSafeReleaseUrl(release.HtmlUrl))
            {
                continue;
            }

            selectedRelease = release;
            selectedVersion = releaseVersion;
        }

        return selectedRelease == null
            ? null
            : new ReleaseUpdateInfo
            {
                VersionTag = selectedRelease.TagName.Trim(),
                Description = SanitizeDescription(selectedRelease.Body),
                ReleaseUrl = selectedRelease.HtmlUrl
            };
    }

    private static HttpClient CreateHttpClient() =>
        new()
        {
            Timeout = TimeSpan.FromSeconds(12),
            MaxResponseContentBufferSize = 512 * 1024
        };

    private static bool IsSafeReleaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value!.Replace("\r", string.Empty);
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = text
            .Replace("**", string.Empty)
            .Replace("__", string.Empty)
            .Replace("`", string.Empty);
        text = Regex.Replace(text, @"(?m)^\s{0,3}[#>]+\s?", string.Empty);
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length <= MaxDescriptionLength
            ? text
            : text.Substring(0, MaxDescriptionLength).TrimEnd() + "…";
    }

    private sealed class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;

        [JsonProperty("draft")]
        public bool Draft { get; set; }

        [JsonProperty("prerelease")]
        public bool PreRelease { get; set; }
    }
}

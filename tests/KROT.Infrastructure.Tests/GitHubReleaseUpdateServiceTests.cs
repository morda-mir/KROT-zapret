using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Infrastructure.Updates;
using Xunit;

namespace KROT.Infrastructure.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNewerNumericReleaseAndDescription()
    {
        const string json = """
            [
              {
                "tag_name": "1.1",
                "html_url": "https://github.com/morda-mir/KROT-zapret/releases/tag/1.1",
                "body": "**Новый релиз**\n\n[Подробнее](https://example.com)",
                "draft": false,
                "prerelease": false
              },
              {
                "tag_name": "1.0",
                "html_url": "https://github.com/morda-mir/KROT-zapret/releases/tag/1.0",
                "body": "",
                "draft": false,
                "prerelease": false
              },
              {
                "tag_name": "9.0",
                "html_url": "https://github.com/morda-mir/KROT-zapret/releases/tag/9.0",
                "body": "",
                "draft": false,
                "prerelease": true
              }
            ]
            """;
        using var client = new HttpClient(new StubHandler(json));
        var service = new GitHubReleaseUpdateService(
            client,
            new Uri("https://example.test/releases"));

        var update = await service.CheckForUpdateAsync(
            "1.0",
            CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("1.1", update!.VersionTag);
        Assert.Contains("Новый релиз", update.Description);
        Assert.DoesNotContain("**", update.Description);
        Assert.DoesNotContain("https://example.com", update.Description);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNullForInstalledRelease()
    {
        const string json = """
            [{
              "tag_name": "1.0",
              "html_url": "https://github.com/morda-mir/KROT-zapret/releases/tag/1.0",
              "body": "",
              "draft": false
            }]
            """;
        using var client = new HttpClient(new StubHandler(json));
        var service = new GitHubReleaseUpdateService(
            client,
            new Uri("https://example.test/releases"));

        var update = await service.CheckForUpdateAsync(
            "1.0",
            CancellationToken.None);

        Assert.Null(update);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }
}

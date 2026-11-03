using System.Net;
using KiNexus.Core.Tests;
using KiNexus.Engines.Updates;

namespace KiNexus.Engines.Tests;

/// <summary>Nach Updates suchen (GitHub Releases), nur auf Klick.</summary>
public class UpdateTests
{
    private const string Release = """
        { "tag_name": "v1.13.0", "name": "KI-Nexus 1.13.0 – Szenen", "body": "### Neu\n- Szenen", "draft": false, "prerelease": false,
          "html_url": "https://github.com/MaelVW/KI-Nexus/releases/tag/v1.13.0", "published_at": "2026-11-04T10:00:00Z" }
        """;

    [Theory]
    [InlineData("v1.12.0", 1, 12, 0)]
    [InlineData("1.12", 1, 12, 0)]
    [InlineData("V2.0.1-beta", 2, 0, 1)]
    [InlineData("1.12.0+abc123", 1, 12, 0)]
    public void ParseVersion_AcceptsTagsAndSuffixes(string text, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), UpdateClient.ParseVersion(text));

    [Fact]
    public void ParseRelease_ReadsGitHubAnswer()
    {
        var release = UpdateClient.ParseRelease(Release)!;

        Assert.Equal("v1.13.0", release.Tag);
        Assert.Equal(new Version(1, 13, 0), release.Version);
        Assert.Equal("KI-Nexus 1.13.0 – Szenen", release.Name);
        Assert.Contains("Szenen", release.Notes, StringComparison.Ordinal);
        Assert.Equal(2026, release.PublishedAt!.Value.Year);
    }

    [Theory]
    [InlineData("\"draft\": true")]
    [InlineData("\"prerelease\": true")]
    public void ParseRelease_IgnoresDraftsAndPreReleases(string flag) =>
        Assert.Null(UpdateClient.ParseRelease("{ \"tag_name\": \"v9.0.0\", " + flag + " }"));

    [Theory]
    [InlineData("1.12.0", UpdateState.Available)]
    [InlineData("1.13.0", UpdateState.UpToDate)]
    [InlineData("1.14.0", UpdateState.UpToDate)]
    public void Compare_FindsNewerVersion(string current, UpdateState expected) =>
        Assert.Equal(expected, UpdateClient.Compare(current, UpdateClient.ParseRelease(Release)).State);

    [Fact]
    public async Task Check_AsksOnlyForTheLatestRelease()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Release);
        using var client = new UpdateClient(handler);

        var check = await client.CheckAsync("1.12.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Available, check.State);
        Assert.Contains("1.13.0", check.Message, StringComparison.Ordinal);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/MaelVW/KI-Nexus/releases/latest", request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Content); // Es wird nichts mitgeschickt.
    }

    [Fact]
    public async Task Check_ExplainsMissingRelease()
    {
        using var client = new UpdateClient(new FakeHandler(HttpStatusCode.NotFound, "{}"));

        var check = await client.CheckAsync("1.12.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.NoRelease, check.State);
        Assert.Contains("privat", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_ReportsNetworkErrors()
    {
        using var client = new UpdateClient(new FakeHandler(null, ""));

        var check = await client.CheckAsync("1.12.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateState.Failed, check.State);
    }

    [Fact]
    public void GitCheckout_IsRecognised()
    {
        using var root = new TempDirectory();
        Assert.False(UpdateClient.IsGitCheckout(root.Path));
        Directory.CreateDirectory(root.Combine(".git"));
        Assert.True(UpdateClient.IsGitCheckout(root.Path));
    }

    /// <summary>Antwortet immer gleich. Status null = keine Verbindung.</summary>
    private sealed class FakeHandler(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return status is { } code
                ? Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) })
                : throw new HttpRequestException("Keine Verbindung");
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TelegramAutoDownload.Headless.Tests.Infrastructure;

namespace TelegramAutoDownload.Headless.Tests;

[Collection("HeadlessApi")]
public sealed class WebAuthApiRegressionTests : IClassFixture<HeadlessWebApplicationFactory>
{
    private readonly HeadlessWebApplicationFactory _factory;

    public WebAuthApiRegressionTests(HeadlessWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Status_Unauthenticated_DoesNotRevealDefaultPasswordState()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/api/web-auth/status");
        body.Should().NotContain("defaultPassword");
        body.Should().NotContain("mustChangePassword");
    }

    [Fact]
    public async Task StaticFile_NotOnAllowlist_Returns401WhenUnauthenticated()
    {
        var wwwroot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var fixture = Path.Combine(wwwroot, "secret-fixture.txt");
        try
        {
            await File.WriteAllTextAsync(fixture, "secret");
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await client.GetAsync("/secret-fixture.txt");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
        }
    }

    [Fact]
    public async Task LoginAssets_AreServedAnonymously()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/index.html")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/app.js")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/app-ui.js")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/styles.css")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/icon.svg")).StatusCode.Should().Be(HttpStatusCode.OK);

        var manifest = await client.GetAsync("/manifest.webmanifest");
        manifest.StatusCode.Should().Be(HttpStatusCode.OK);
        manifest.Content.Headers.ContentType!.MediaType.Should().Be("application/manifest+json");

        var script = await client.GetAsync("/app.js");
        script.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task Response_IncludesSecurityHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/web-auth/status");
        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    [Fact]
    public async Task DownloadFolder_RejectsAbsolutePathOutsideAllowedRoot()
    {
        var client = await CreateAuthenticatedClientAsync();
        var before = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        var beforeFolder = before.GetProperty("downloadFolder").GetString();

        foreach (var bad in new[] { "/etc", "/data", @"C:\Windows" })
        {
            var response = await client.PostAsJsonAsync("/api/settings/download-folder", new { folder = bad });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var after = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        after.GetProperty("downloadFolder").GetString().Should().Be(beforeFolder);
    }

    [Theory]
    [InlineData("socialDownloadFolderTemplate", "../escape")]
    [InlineData("youtubeDownloadFolderTemplate", "/etc")]
    [InlineData("otherDownloadFolderTemplate", @"C:\Windows")]
    [InlineData("torrentDownloadFolderTemplate", "a/../../b")]
    public async Task ChatFeatures_RejectsUnsafeProviderFolderTemplates(string field, string template)
    {
        var client = await CreateAuthenticatedClientAsync();
        var body = new Dictionary<string, string> { [field] = template };
        var response = await client.PatchAsJsonAsync("/api/chats/1/features", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/web-auth/login", new
        {
            username = HeadlessWebApplicationFactory.TestUsername,
            password = HeadlessWebApplicationFactory.TestPassword,
        });
        login.EnsureSuccessStatusCode();
        return client;
    }
}

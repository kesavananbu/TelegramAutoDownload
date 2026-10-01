using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TelegramAutoDownload.Headless.Tests.Infrastructure;

namespace TelegramAutoDownload.Headless.Tests;

[Collection("HeadlessApi")]
public sealed class WebAuthApiSmokeTests : IClassFixture<HeadlessWebApplicationFactory>
{
    private readonly HeadlessWebApplicationFactory _factory;

    public WebAuthApiSmokeTests(HeadlessWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Protected_endpoint_returns_401_without_session()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/chats");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Web_auth_status_is_public_without_cookie()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/web-auth/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("enabled").GetBoolean().Should().BeTrue();
        json.GetProperty("authenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Login_with_invalid_password_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/web-auth/login", new
        {
            username = HeadlessWebApplicationFactory.TestUsername,
            password = "wrong-password-value",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_succeeds_and_allows_protected_api_access()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/web-auth/login", new
        {
            username = HeadlessWebApplicationFactory.TestUsername,
            password = HeadlessWebApplicationFactory.TestPassword,
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var chats = await client.GetAsync("/api/chats");
        chats.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [CollectionDefinition("HeadlessApi", DisableParallelization = true)]
    public sealed class HeadlessApiCollection;
}

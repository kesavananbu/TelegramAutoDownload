using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TelegramAutoDownload.Headless;

namespace TelegramAutoDownload.Headless.Tests;

[Collection("HeadlessAuth")]
public sealed class WebAuthServiceTests : IDisposable
{
    private readonly string _credentialsFile;
    private readonly string _secretFile;

    public WebAuthServiceTests()
    {
        _credentialsFile = Path.Combine(HeadlessPaths.DataDir, "web-auth.json");
        _secretFile = Path.Combine(HeadlessPaths.DataDir, ".web-auth-secret");
        CleanupAuthFiles();
        Environment.SetEnvironmentVariable("WEB_AUTH_ENABLED", "true");
        Environment.SetEnvironmentVariable("WEB_USERNAME", "charuser");
        Environment.SetEnvironmentVariable("WEB_PASSWORD", "charpass12345");
        Environment.SetEnvironmentVariable("WEB_AUTH_SECRET", "characterization-signing-key");
    }

    public void Dispose() => CleanupAuthFiles();

    private void CleanupAuthFiles()
    {
        if (File.Exists(_credentialsFile)) File.Delete(_credentialsFile);
        if (File.Exists(_secretFile)) File.Delete(_secretFile);
    }

    [Fact]
    public void IsPublicPath_allows_static_assets_and_login_endpoints()
    {
        WebAuthService.IsPublicPath("/").Should().BeTrue();
        WebAuthService.IsPublicPath("/app.js").Should().BeTrue();
        WebAuthService.IsPublicPath("/api/web-auth/status").Should().BeTrue();
        WebAuthService.IsPublicPath("/api/web-auth/login").Should().BeTrue();
        WebAuthService.IsPublicPath("/api/chats").Should().BeFalse();
    }

    [Fact]
    public void Load_marks_default_password_when_web_password_env_unset()
    {
        try
        {
            Environment.SetEnvironmentVariable("WEB_PASSWORD", null);
            var auth = WebAuthService.Load();
            auth.Enabled.Should().BeTrue();
            auth.Username.Should().Be("charuser");
            auth.UsingDefaultPassword.Should().BeTrue();
            auth.ValidateCredentials("charuser", "changeme").Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEB_PASSWORD", "charpass12345");
        }
    }

    [Fact]
    public void ValidateCredentials_rejects_wrong_username_or_password()
    {
        var auth = WebAuthService.Load();
        auth.ValidateCredentials("wrong", "charpass12345").Should().BeFalse();
        auth.ValidateCredentials("charuser", "wrong").Should().BeFalse();
    }

    [Fact]
    public void SignIn_and_TryGetSession_round_trip_valid_cookie()
    {
        var auth = WebAuthService.Load();
        var http = new DefaultHttpContext();
        auth.SignIn(http.Response, auth.Username);

        var setCookie = http.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain(WebAuthService.CookieName);

        var request = new DefaultHttpContext();
        request.Request.Headers.Cookie = CookieHeaderValueOnly(setCookie);

        auth.TryGetSession(request.Request, out var user).Should().BeTrue();
        user.Should().Be("charuser");
    }

    [Fact]
    public void TryGetSession_rejects_tampered_signature()
    {
        var auth = WebAuthService.Load();
        var http = new DefaultHttpContext();
        auth.SignIn(http.Response, auth.Username);
        var cookie = CookieHeaderValueOnly(http.Response.Headers.SetCookie.ToString());
        var tampered = cookie[..^4] + "xxxx";

        var request = new DefaultHttpContext();
        request.Request.Headers.Cookie = tampered;
        auth.TryGetSession(request.Request, out _).Should().BeFalse();
    }

    [Fact]
    public void TryChangePassword_enforces_minimum_length_and_rejects_default()
    {
        var auth = WebAuthService.Load();
        auth.TryChangePassword("charpass12345", "short", out var err1).Should().BeFalse();
        err1.Should().Contain("8 characters");

        auth.TryChangePassword("charpass12345", "changeme", out var err2).Should().BeFalse();
        err2.Should().Contain("default");

        auth.TryChangePassword("charpass12345", "newpass98765", out _).Should().BeTrue();
        auth.UsingDefaultPassword.Should().BeFalse();
        auth.ValidateCredentials("charuser", "newpass98765").Should().BeTrue();
    }

    private static string CookieHeaderValueOnly(string setCookieHeader)
    {
        var semicolon = setCookieHeader.IndexOf(';', StringComparison.Ordinal);
        return semicolon >= 0 ? setCookieHeader[..semicolon] : setCookieHeader;
    }

    [CollectionDefinition("HeadlessAuth", DisableParallelization = true)]
    public sealed class HeadlessAuthCollection;
}

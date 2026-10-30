using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using TelegramAutoDownload.Headless;
using TelegramAutoDownload.Headless.Security;

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
        Environment.SetEnvironmentVariable("WEB_PASSWORD_RESET", null);
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
    public void Load_NoPasswordConfigured_DoesNotAcceptChangeme()
    {
        try
        {
            Environment.SetEnvironmentVariable("WEB_PASSWORD", null);
            var auth = WebAuthService.Load();
            auth.Enabled.Should().BeTrue();
            auth.ValidateCredentials("admin", "changeme").Should().BeFalse();
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
        auth.SignIn(http, auth.Username);

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
        auth.SignIn(http, auth.Username);
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
        auth.MustChangePassword.Should().BeFalse();
        auth.ValidateCredentials("charuser", "newpass98765").Should().BeTrue();
    }

    [Fact]
    public void HashPassword_SameInputTwice_ProducesDifferentOutput()
    {
        PasswordHasher.HashPassword("same-password-value").Should()
            .NotBe(PasswordHasher.HashPassword("same-password-value"));
    }

    [Fact]
    public void LoadCredentials_V1Sha256File_StillAuthenticates_ThenUpgradesToV2()
    {
        var legacyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("charpass12345")));
        var file = new
        {
            Username = "charuser",
            PasswordHashBase64 = legacyHash,
            SessionEpoch = 1L,
        };
        File.WriteAllText(_credentialsFile, JsonConvert.SerializeObject(file));
        Environment.SetEnvironmentVariable("WEB_PASSWORD", null);

        var auth = WebAuthService.Load();
        auth.ValidateCredentials("charuser", "charpass12345").Should().BeTrue();

        var saved = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(_credentialsFile))!;
        ((string)saved.PasswordHash).Should().StartWith("pbkdf2-sha256$");
    }

    [Fact]
    public void ChangePassword_InvalidatesExistingSessions()
    {
        var auth = WebAuthService.Load();
        var http = new DefaultHttpContext();
        auth.SignIn(http, auth.Username);
        var cookie = CookieHeaderValueOnly(http.Response.Headers.SetCookie.ToString());

        auth.TryChangePassword("charpass12345", "newpass98765", out _).Should().BeTrue();

        var request = new DefaultHttpContext();
        request.Request.Headers.Cookie = cookie;
        auth.TryGetSession(request.Request, out _).Should().BeFalse();
    }

    [Fact]
    public void SignOut_InvalidatesCookieServerSide()
    {
        var auth = WebAuthService.Load();
        var signIn = new DefaultHttpContext();
        auth.SignIn(signIn, auth.Username);
        var cookie = CookieHeaderValueOnly(signIn.Response.Headers.SetCookie.ToString());

        var logout = new DefaultHttpContext();
        auth.SignOut(logout.Response);

        var replay = new DefaultHttpContext();
        replay.Request.Headers.Cookie = cookie;
        auth.TryGetSession(replay.Request, out _).Should().BeFalse();
    }

    [Fact]
    public void ValidateCredentials_WrongUsername_StillPerformsKdf()
    {
        var auth = WebAuthService.Load();
        var swUser = Stopwatch.StartNew();
        auth.ValidateCredentials("not-the-user", "charpass12345");
        swUser.Stop();

        var swPass = Stopwatch.StartNew();
        auth.ValidateCredentials("charuser", "not-the-password");
        swPass.Stop();

        swUser.Elapsed.Should().BeCloseTo(swPass.Elapsed, TimeSpan.FromMilliseconds(swPass.Elapsed.TotalMilliseconds * 0.35 + 40));
    }

    private static string CookieHeaderValueOnly(string setCookieHeader)
    {
        var semicolon = setCookieHeader.IndexOf(';', StringComparison.Ordinal);
        return semicolon >= 0 ? setCookieHeader[..semicolon] : setCookieHeader;
    }

    [CollectionDefinition("HeadlessAuth", DisableParallelization = true)]
    public sealed class HeadlessAuthCollection;
}

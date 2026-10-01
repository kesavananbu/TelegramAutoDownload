using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace TelegramAutoDownload.Headless.Tests.Infrastructure;

/// <summary>
/// Boots the real headless host with auth enabled and isolated data paths.
/// </summary>
public sealed class HeadlessWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestUsername = "testadmin";
    public const string TestPassword = "testpass12345";

    static HeadlessWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("WEB_AUTH_ENABLED", "true");
        Environment.SetEnvironmentVariable("WEB_USERNAME", TestUsername);
        Environment.SetEnvironmentVariable("WEB_PASSWORD", TestPassword);
        Environment.SetEnvironmentVariable("WEB_AUTH_SECRET", "headless-integration-test-signing-secret");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        return base.CreateHost(builder);
    }
}

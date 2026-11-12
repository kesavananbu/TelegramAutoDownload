using System;

namespace TelegramClient;

/// <summary>
/// Reads Docker / NAS settings. Prefixed names (e.g. <c>TELEGRAMDOWNLOAD_WEB_PASSWORD</c>)
/// avoid clashes in a shared <c>secret.env</c>; unprefixed names remain supported.
/// </summary>
public static class EnvVars
{
    public const string Prefix = "TELEGRAMDOWNLOAD_";

    public static string? Get(string name) =>
        Environment.GetEnvironmentVariable(Prefix + name)
        ?? Environment.GetEnvironmentVariable(name);

    public static bool Equals(string name, string value, StringComparison comparison) =>
        string.Equals(Get(name), value, comparison);
}

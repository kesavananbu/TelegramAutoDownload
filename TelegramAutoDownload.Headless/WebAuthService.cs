using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Serilog;
using TelegramAutoDownload.Headless.Security;

namespace TelegramAutoDownload.Headless;

/// <summary>
/// Cookie session gate for the headless web UI. Credentials from <c>/data/web-auth.json</c>
/// or WEB_USERNAME / WEB_PASSWORD env vars (with explicit reset semantics).
/// </summary>
public sealed class WebAuthService
{
    public const string CookieName = "tad_web";
    private static readonly HashSet<string> RejectedPasswords =
        new(StringComparer.OrdinalIgnoreCase) { "changeme" };

    private static readonly string CredentialsFile = Path.Combine(HeadlessPaths.DataDir, "web-auth.json");

    public bool Enabled { get; }
    public bool MustChangePassword => _mustChangePassword;
    public bool UsingDefaultPassword => _mustChangePassword;
    public string Username => _username;
    public string CredentialSource => _credentialSource;

    private readonly object _lock = new();
    private readonly byte[] _signingKey;
    private readonly TimeSpan _sessionLifetime;
    private readonly string _dummyPasswordHash = PasswordHasher.DummyHashForTiming;

    private string _username;
    private string _passwordHash;
    private bool _mustChangePassword;
    private long _sessionEpoch;
    private string _credentialSource;

    private WebAuthService(
        bool enabled,
        string username,
        string passwordHash,
        byte[] signingKey,
        TimeSpan sessionLifetime,
        bool mustChangePassword,
        long sessionEpoch,
        string credentialSource)
    {
        Enabled = enabled;
        _username = username;
        _passwordHash = passwordHash;
        _signingKey = signingKey;
        _sessionLifetime = sessionLifetime;
        _mustChangePassword = mustChangePassword;
        _sessionEpoch = sessionEpoch;
        _credentialSource = credentialSource;
    }

    public static WebAuthService Load()
    {
        var enabled = !string.Equals(
            Environment.GetEnvironmentVariable("WEB_AUTH_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);

        if (!enabled)
        {
            EnsureAuthDisableAllowed();
            Log.Warning(
                "WEB AUTH IS DISABLED — every dashboard endpoint is anonymous. " +
                "Do not expose this instance on a network.");
            return new WebAuthService(false, "", "", Array.Empty<byte>(), TimeSpan.Zero, false, 0, "disabled");
        }

        var days = 7;
        if (int.TryParse(Environment.GetEnvironmentVariable("WEB_SESSION_DAYS"), out var d) && d > 0 && d <= 365)
            days = d;

        var signingKey = LoadOrCreateSigningKey();
        var creds = LoadCredentials();

        Log.Information(
            "Web UI authentication enabled for user {Username} (session {Days}d, credentials from {Source}).",
            creds.Username, days, creds.Source);

        return new WebAuthService(
            true,
            creds.Username,
            creds.PasswordHash,
            signingKey,
            TimeSpan.FromDays(days),
            creds.MustChangePassword,
            creds.SessionEpoch,
            creds.Source);
    }

    public bool ValidateCredentials(string username, string password)
    {
        if (!Enabled) return true;
        lock (_lock)
        {
            var userOk = UsernamesEqual(username, _username);
            var hash = userOk ? _passwordHash : _dummyPasswordHash;
            var passOk = PasswordHasher.Verify(password ?? "", hash);
            var ok = userOk & passOk;
            if (ok && PasswordHasher.IsLegacySha256Base64(_passwordHash))
                UpgradePasswordHash(password ?? "");
            return ok;
        }
    }

    public bool TryChangePassword(string currentPassword, string newPassword, out string? error)
    {
        error = null;
        if (!Enabled)
        {
            error = "Web authentication is disabled.";
            return false;
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            error = "Current password is required.";
            return false;
        }

        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            error = "New password must be at least 8 characters.";
            return false;
        }

        if (IsRejectedPassword(newPassword))
        {
            error = "Choose a password other than the default \"changeme\".";
            return false;
        }

        lock (_lock)
        {
            if (!PasswordHasher.Verify(currentPassword, _passwordHash))
            {
                error = "Current password is incorrect.";
                return false;
            }

            _passwordHash = PasswordHasher.HashPassword(newPassword);
            _mustChangePassword = false;
            BumpSessionEpochLocked();
            SaveCredentialsLocked();
        }

        Log.Information("Web dashboard password updated for user {Username}.", _username);
        return true;
    }

    public void SignIn(HttpContext httpContext, string username)
    {
        var response = httpContext.Response;
        var expires = DateTimeOffset.UtcNow.Add(_sessionLifetime).ToUnixTimeSeconds();
        long epoch;
        lock (_lock)
            epoch = _sessionEpoch;

        var payload = $"{username}|{epoch}|{expires}";
        var sig = Sign(payload);
        var value = $"{payload}|{sig}";
        response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ShouldUseSecureCookies(httpContext.Request),
            Path = "/",
            MaxAge = _sessionLifetime,
            IsEssential = true,
        });
    }

    public void SignOut(HttpResponse response)
    {
        lock (_lock)
        {
            BumpSessionEpochLocked();
            SaveCredentialsLocked();
        }

        response.Cookies.Delete(CookieName, new CookieOptions
        {
            Path = "/",
            Secure = true,
        });
    }

    public void RevokeAllSessions()
    {
        lock (_lock)
        {
            BumpSessionEpochLocked();
            SaveCredentialsLocked();
        }
    }

    public bool TryGetSession(HttpRequest request, out string? username)
    {
        username = null;
        if (!Enabled) return true;
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
            return false;

        var parts = value.Split('|', 4);
        if (parts.Length != 4) return false;

        var user = parts[0];
        if (!long.TryParse(parts[1], out var epoch)) return false;
        if (!long.TryParse(parts[2], out var expires)) return false;
        var sig = parts[3];

        var payload = $"{user}|{epoch}|{expires}";
        if (!ConstantTimeEquals(Sign(payload), sig)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return false;

        lock (_lock)
        {
            if (epoch < _sessionEpoch) return false;
        }

        username = user;
        return true;
    }

    public static bool IsPublicPath(PathString path)
    {
        var p = path.Value ?? "";
        if (p is "/" or "/index.html" or "/styles.css" or "/app.js" or "/app-ui.js")
            return true;
        if (p.Equals("/api/web-auth/status", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.Equals("/api/web-auth/login", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool IsAllowedWhilePasswordChangePending(PathString path)
    {
        var p = path.Value ?? "";
        if (IsPublicPath(path)) return true;
        if (p.Equals("/api/web-auth/change-password", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.Equals("/api/web-auth/logout", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.Equals("/api/web-auth/logout-all", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.Equals("/api/web-auth/status", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool ShouldUseSecureCookies(HttpRequest request)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("WEB_AUTH_INSECURE_COOKIES"), "true", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("WEB_AUTH_INSECURE_COOKIES is ignored in Production.");
            }
            else
            {
                Log.Warning("WEB_AUTH_INSECURE_COOKIES=true — session cookies are not marked Secure.");
                return false;
            }
        }

        if (request.IsHttps) return true;
        if (request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
            && proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(
                Environment.GetEnvironmentVariable("WEB_AUTH_SECURE_COOKIES"), "true", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static (string Username, string PasswordHash, bool MustChangePassword, long SessionEpoch, string Source)
        LoadCredentials()
    {
        var envPassword = Environment.GetEnvironmentVariable("WEB_PASSWORD");
        var envUsername = Environment.GetEnvironmentVariable("WEB_USERNAME")?.Trim();
        var resetFromEnv = string.Equals(
            Environment.GetEnvironmentVariable("WEB_PASSWORD_RESET"), "true", StringComparison.OrdinalIgnoreCase);

        if (File.Exists(CredentialsFile))
        {
            try
            {
                var saved = JsonConvert.DeserializeObject<WebAuthCredentialsFile>(File.ReadAllText(CredentialsFile));
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Username))
                {
                    var hash = ResolveStoredHash(saved);
                    if (hash != null)
                    {
                        if (!string.IsNullOrEmpty(envPassword))
                        {
                            if (IsRejectedPassword(envPassword))
                                throw new InvalidOperationException(
                                    "WEB_PASSWORD is set to a rejected value (e.g. \"changeme\"). Choose a strong password.");

                            var envMatches = PasswordHasher.Verify(envPassword, hash);
                            if (!envMatches)
                            {
                                if (!resetFromEnv)
                                {
                                    throw new InvalidOperationException(
                                        "WEB_PASSWORD does not match stored credentials. " +
                                        "Set WEB_PASSWORD_RESET=true to rotate from the environment, " +
                                        "or delete web-auth.json.");
                                }

                                hash = PasswordHasher.HashPassword(envPassword);
                                saved.PasswordHash = hash;
                                saved.PasswordHashBase64 = null;
                                saved.SessionEpoch++;
                                saved.MustChangePassword = false;
                                WriteCredentialsFile(saved);
                                Log.Information("Web credentials rotated from WEB_PASSWORD (WEB_PASSWORD_RESET=true).");
                                return (
                                    saved.Username.Trim(),
                                    hash,
                                    saved.MustChangePassword,
                                    saved.SessionEpoch,
                                    "environment-reset");
                            }
                        }

                        return (
                            saved.Username.Trim(),
                            hash,
                            saved.MustChangePassword,
                            saved.SessionEpoch,
                            "web-auth.json");
                    }
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read {Path} — falling back to environment credentials.", CredentialsFile);
            }
        }

        var username = string.IsNullOrEmpty(envUsername) ? "admin" : envUsername;

        if (!string.IsNullOrEmpty(envPassword))
        {
            if (IsRejectedPassword(envPassword))
                throw new InvalidOperationException(
                    "WEB_PASSWORD is set to a rejected value (e.g. \"changeme\"). Choose a strong password.");

            var hash = PasswordHasher.HashPassword(envPassword);
            var file = new WebAuthCredentialsFile
            {
                Username = username,
                PasswordHash = hash,
                MustChangePassword = false,
                SessionEpoch = 1,
            };
            WriteCredentialsFile(file);
            return (username, hash, false, 1, "environment");
        }

        var generated = RandomNumberGenerator.GetString(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 24);
        WriteInitialPasswordBanner(generated);
        var generatedHash = PasswordHasher.HashPassword(generated);
        var bootstrap = new WebAuthCredentialsFile
        {
            Username = username,
            PasswordHash = generatedHash,
            MustChangePassword = true,
            SessionEpoch = 1,
        };
        WriteCredentialsFile(bootstrap);
        return (username, generatedHash, true, 1, "generated");
    }

    private static string? ResolveStoredHash(WebAuthCredentialsFile saved)
    {
        if (!string.IsNullOrWhiteSpace(saved.PasswordHash))
            return saved.PasswordHash;
        if (!string.IsNullOrWhiteSpace(saved.PasswordHashBase64))
            return saved.PasswordHashBase64;
        return null;
    }

    private void UpgradePasswordHash(string password)
    {
        _passwordHash = PasswordHasher.HashPassword(password);
        SaveCredentialsLocked();
        Log.Information("Upgraded web dashboard password hash to PBKDF2 for user {Username}.", _username);
    }

    private void BumpSessionEpochLocked()
    {
        _sessionEpoch++;
    }

    private void SaveCredentialsLocked()
    {
        var file = new WebAuthCredentialsFile
        {
            Username = _username,
            PasswordHash = _passwordHash,
            PasswordHashBase64 = null,
            MustChangePassword = _mustChangePassword,
            SessionEpoch = _sessionEpoch,
        };
        WriteCredentialsFile(file);
    }

    private static void WriteCredentialsFile(WebAuthCredentialsFile payload) =>
        SecretFileWriter.WriteSecret(CredentialsFile, JsonConvert.SerializeObject(payload, Formatting.Indented));

    private static void WriteInitialPasswordBanner(string password)
    {
        Console.Out.WriteLine();
        Console.Out.WriteLine(new string('=', 78));
        Console.Out.WriteLine("WEB DASHBOARD INITIAL PASSWORD (shown once — not written to log files):");
        Console.Out.WriteLine($"  {password}");
        Console.Out.WriteLine("Sign in and change this password immediately.");
        Console.Out.WriteLine(new string('=', 78));
        Console.Out.WriteLine();
    }

    private static byte[] LoadOrCreateSigningKey()
    {
        var fromEnv = Environment.GetEnvironmentVariable("WEB_AUTH_SECRET");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return SHA256.HashData(Encoding.UTF8.GetBytes(fromEnv));

        var path = Path.Combine(HeadlessPaths.DataDir, ".web-auth-secret");
        if (File.Exists(path))
            return Convert.FromBase64String(File.ReadAllText(path).Trim());

        var key = RandomNumberGenerator.GetBytes(32);
        SecretFileWriter.WriteSecret(path, Convert.ToBase64String(key));
        Log.Information("Generated web auth signing secret at {Path}", path);
        return key;
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static bool UsernamesEqual(string? input, string expected)
    {
        input = input?.Trim() ?? "";
        var a = Encoding.UTF8.GetBytes(input);
        var b = Encoding.UTF8.GetBytes(expected);
        var lenMatch = a.Length == b.Length;
        var max = Math.Max(a.Length, b.Length);
        var diff = 0;
        for (var i = 0; i < max; i++)
        {
            var ai = i < a.Length ? a[i] : (byte)0;
            var bi = i < b.Length ? b[i] : (byte)0;
            diff |= ai ^ bi;
        }

        return lenMatch && diff == 0;
    }

    private static bool IsRejectedPassword(string password) =>
        RejectedPasswords.Contains(password);

    private static void EnsureAuthDisableAllowed()
    {
        if (IsLoopbackBind()) return;

        if (string.Equals(
                Environment.GetEnvironmentVariable("WEB_AUTH_DISABLE_I_UNDERSTAND"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "WEB_AUTH_DISABLE_I_UNDERSTAND=true — authentication disabled on a non-loopback bind address.");
            return;
        }

        throw new InvalidOperationException(
            "WEB_AUTH_ENABLED=false is only allowed when LISTEN_URL binds to loopback " +
            "(127.0.0.1 / localhost), or when WEB_AUTH_DISABLE_I_UNDERSTAND=true.");
    }

    private static bool IsLoopbackBind()
    {
        var listen = Environment.GetEnvironmentVariable("LISTEN_URL")
                     ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
                     ?? "http://0.0.0.0:8080";
        foreach (var part in listen.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(part, UriKind.Absolute, out var uri)) continue;
            if (uri.Host is "127.0.0.1" or "localhost" or "[::1]")
                return true;
        }

        return false;
    }

    private sealed class WebAuthCredentialsFile
    {
        public int Version { get; set; } = 2;
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string? PasswordHashBase64 { get; set; }
        public bool MustChangePassword { get; set; }
        public long SessionEpoch { get; set; } = 1;
    }
}

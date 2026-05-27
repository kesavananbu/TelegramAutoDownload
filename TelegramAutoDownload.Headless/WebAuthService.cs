using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Serilog;

namespace TelegramAutoDownload.Headless;

/// <summary>
/// Cookie session gate for the headless web UI. Credentials from <c>/data/web-auth.json</c>
/// (after first change) or WEB_USERNAME / WEB_PASSWORD env vars.
/// </summary>
public sealed class WebAuthService
{
    public const string CookieName = "tad_web";
    private const string DefaultPassword = "changeme";

    private static readonly string CredentialsFile = Path.Combine(HeadlessPaths.DataDir, "web-auth.json");

    public bool Enabled { get; }
    public bool UsingDefaultPassword => _usingDefaultPassword;
    public string Username => _username;

    private readonly object _lock = new();
    private readonly byte[] _signingKey;
    private readonly TimeSpan _sessionLifetime;

    private string _username;
    private byte[] _passwordHash;
    private bool _usingDefaultPassword;

    private WebAuthService(
        bool enabled,
        string username,
        byte[] passwordHash,
        byte[] signingKey,
        TimeSpan sessionLifetime,
        bool usingDefaultPassword)
    {
        Enabled = enabled;
        _username = username;
        _passwordHash = passwordHash;
        _signingKey = signingKey;
        _sessionLifetime = sessionLifetime;
        _usingDefaultPassword = usingDefaultPassword;
    }

    public static WebAuthService Load()
    {
        var enabled = !string.Equals(
            Environment.GetEnvironmentVariable("WEB_AUTH_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);

        if (!enabled)
        {
            Log.Information("Web UI authentication is disabled (WEB_AUTH_ENABLED=false).");
            return new WebAuthService(false, "", [], [], TimeSpan.Zero, false);
        }

        var days = 7;
        if (int.TryParse(Environment.GetEnvironmentVariable("WEB_SESSION_DAYS"), out var d) && d > 0 && d <= 365)
            days = d;

        var signingKey = LoadOrCreateSigningKey();
        var (username, hash, usingDefault) = LoadCredentials();

        Log.Information("Web UI authentication enabled for user {Username} (session {Days}d).", username, days);
        if (usingDefault)
        {
            Log.Warning(
                "Default web password \"changeme\" is active — change it from the dashboard or set WEB_PASSWORD.");
        }

        return new WebAuthService(true, username, hash, signingKey, TimeSpan.FromDays(days), usingDefault);
    }

    public bool ValidateCredentials(string username, string password)
    {
        if (!Enabled) return true;
        lock (_lock)
        {
            if (!string.Equals(username?.Trim(), _username, StringComparison.Ordinal))
                return false;
            return CryptographicOperations.FixedTimeEquals(HashPassword(password ?? ""), _passwordHash);
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

        if (string.Equals(newPassword, DefaultPassword, StringComparison.Ordinal))
        {
            error = "Choose a password other than the default \"changeme\".";
            return false;
        }

        lock (_lock)
        {
            if (!CryptographicOperations.FixedTimeEquals(HashPassword(currentPassword), _passwordHash))
            {
                error = "Current password is incorrect.";
                return false;
            }

            _passwordHash = HashPassword(newPassword);
            _usingDefaultPassword = false;
            SaveCredentials(_username, _passwordHash);
        }

        Log.Information("Web dashboard password updated for user {Username}.", _username);
        return true;
    }

    public void SignIn(HttpResponse response, string username)
    {
        var expires = DateTimeOffset.UtcNow.Add(_sessionLifetime).ToUnixTimeSeconds();
        var payload = $"{username}|{expires}";
        var sig = Sign(payload);
        var value = $"{payload}|{sig}";
        response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = string.Equals(Environment.GetEnvironmentVariable("WEB_AUTH_SECURE_COOKIES"), "true", StringComparison.OrdinalIgnoreCase),
            Path = "/",
            MaxAge = _sessionLifetime,
            IsEssential = true,
        });
    }

    public void SignOut(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });

    public bool TryGetSession(HttpRequest request, out string? username)
    {
        username = null;
        if (!Enabled) return true;
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
            return false;

        var parts = value.Split('|', 3);
        if (parts.Length != 3) return false;

        var user = parts[0];
        if (!long.TryParse(parts[1], out var expires)) return false;
        var sig = parts[2];

        var payload = $"{user}|{expires}";
        if (!ConstantTimeEquals(Sign(payload), sig)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return false;

        username = user;
        return true;
    }

    public static bool IsPublicPath(PathString path)
    {
        var p = path.Value ?? "";
        if (p is "/" or "/index.html" or "/styles.css" or "/app.js")
            return true;
        if (p.Equals("/api/web-auth/status", StringComparison.OrdinalIgnoreCase))
            return true;
        if (p.Equals("/api/web-auth/login", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static (string Username, byte[] Hash, bool UsingDefault) LoadCredentials()
    {
        if (File.Exists(CredentialsFile))
        {
            try
            {
                var saved = JsonConvert.DeserializeObject<WebAuthCredentialsFile>(File.ReadAllText(CredentialsFile));
                if (saved != null
                    && !string.IsNullOrWhiteSpace(saved.Username)
                    && !string.IsNullOrWhiteSpace(saved.PasswordHashBase64))
                {
                    var hash = Convert.FromBase64String(saved.PasswordHashBase64);
                    var usingDefault = CryptographicOperations.FixedTimeEquals(hash, HashPassword(DefaultPassword));
                    return (saved.Username.Trim(), hash, usingDefault);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read {Path} — falling back to environment credentials.", CredentialsFile);
            }
        }

        var username = Environment.GetEnvironmentVariable("WEB_USERNAME")?.Trim();
        if (string.IsNullOrEmpty(username))
            username = "admin";

        var password = Environment.GetEnvironmentVariable("WEB_PASSWORD");
        var envUnset = string.IsNullOrEmpty(password);
        if (envUnset)
            password = DefaultPassword;

        var usingDefaultPassword = envUnset || string.Equals(password, DefaultPassword, StringComparison.Ordinal);
        return (username, HashPassword(password ?? DefaultPassword), usingDefaultPassword);
    }

    private static void SaveCredentials(string username, byte[] hash)
    {
        var payload = new WebAuthCredentialsFile
        {
            Username = username,
            PasswordHashBase64 = Convert.ToBase64String(hash),
        };
        Directory.CreateDirectory(HeadlessPaths.DataDir);
        File.WriteAllText(CredentialsFile, JsonConvert.SerializeObject(payload, Formatting.Indented));
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
        Directory.CreateDirectory(HeadlessPaths.DataDir);
        File.WriteAllText(path, Convert.ToBase64String(key));
        Log.Information("Generated web auth signing secret at {Path}", path);
        return key;
    }

    private static byte[] HashPassword(string password) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(password));

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

    private sealed class WebAuthCredentialsFile
    {
        public string Username { get; set; } = "";
        public string PasswordHashBase64 { get; set; } = "";
    }
}

using Serilog;
using TelegramClient;

namespace TelegramAutoDownload.Headless;

public enum LoginStage
{
    NotStarted,
    AwaitingPhone,
    AwaitingCode,
    AwaitingPassword,
    LoggedIn,
    Failed,
}

/// <summary>
/// Web-facing state machine around WTelegramClient.Client.Login(string).
/// Each call advances by one step:
///   1. Login(phone)    → server sends code; stage = AwaitingCode
///   2. Login(code)     → either LoggedIn, or AwaitingPassword if 2FA
///   3. Login(password) → LoggedIn (when 2FA is enabled)
/// </summary>
public sealed class LoginCoordinator
{
    private readonly HeadlessHost _host;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LoginCoordinator(HeadlessHost host) { _host = host; }

    public LoginStage Stage { get; private set; } = LoginStage.NotStarted;
    public string? LastError { get; private set; }
    public long UserId => _host.Telegram?.Client.UserId ?? 0;
    public bool IsLoggedIn => UserId != 0;

    /// <summary>
    /// Refresh stage by probing the underlying WTelegram session. Called once on startup
    /// so a restored session is reflected without the user filling the form.
    /// </summary>
    public async Task ProbeAsync()
    {
        try
        {
            await _host.EnsureTelegramAsync();
            // Give the background login a moment to restore the session
            await Task.Delay(800);
            Stage = IsLoggedIn ? LoginStage.LoggedIn : LoginStage.AwaitingPhone;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stage = LoginStage.Failed;
            Log.Warning(ex, "ProbeAsync failed");
        }
    }

    public async Task<bool> SubmitPhoneAsync(string phone)
    {
        return await SubmitAsync(phone.StartsWith('+') ? phone : "+" + phone, LoginStage.AwaitingCode);
    }

    public Task<bool> SubmitCodeAsync(string code)        => SubmitAsync(code,     LoginStage.AwaitingCode);
    public Task<bool> SubmitPasswordAsync(string pwd)     => SubmitAsync(pwd,      LoginStage.AwaitingPassword);

    private async Task<bool> SubmitAsync(string value, LoginStage currentExpected)
    {
        await _gate.WaitAsync();
        try
        {
            LastError = null;
            await _host.EnsureTelegramAsync();
            var nextField = await _host.Telegram!.Client.Login(value);
            // WTelegramClient returns null on success; otherwise the name of the next required field
            // (e.g. "verification_code", "password"). Map that to a stage.
            Stage = nextField switch
            {
                null                       => LoginStage.LoggedIn,
                "verification_code"        => LoginStage.AwaitingCode,
                "password"                 => LoginStage.AwaitingPassword,
                _                          => LoginStage.AwaitingPhone,
            };

            if (Stage == LoginStage.LoggedIn)
                await _host.OnLoggedInAsync();

            return Stage == LoginStage.LoggedIn || Stage == currentExpected;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stage = LoginStage.Failed;
            Log.Warning(ex, "Login submit failed");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LogoutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await _host.LogoutAsync();
            Stage = LoginStage.AwaitingPhone;
            LastError = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}

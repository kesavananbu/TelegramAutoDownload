# Task T4 — Headless web security remediation

**Branch:** `er/t4-web-security` (from `enterprise-readiness` @ a458fd3)  
**Worktree:** `TelegramDownloader-t4`

## Commits (finding IDs)

| SHA | Message | Findings |
|-----|---------|----------|
| 1579a1e | security(F-07): atomic secret writes… | F-07 |
| 46f4565 | security(F-03,F-23): PBKDF2… (+ WebAuthService/Middleware) | F-01, F-03, F-04, F-08, F-20, F-23 |
| 09ad8b3 | security(F-02): constrain download paths… | F-02 |
| 6759e41 | security(F-05,F-06,F-17,F-18): auth pipeline… (+ compose/wwwroot) | F-01, F-05, F-06, F-17, F-18 |
| 604ef37 | docs + regression tests | tests for all in-scope IDs |

## Verification

```powershell
dotnet build RunnerApp.sln -c Release
dotnet test TelegramAutoDownload.Headless.Tests -c Release
```

**Headless tests:** 36 passed (prior baseline 22; characterization tests updated for PBKDF2, no default `changeme`, session epoch, and pre-auth status shape).

## README changes needed

- Document **required** `WEB_PASSWORD` in `.env` for Compose; removed `changeme` default.
- Document **bootstrap password**: when neither `WEB_PASSWORD` nor `web-auth.json` exists, a one-time generated password is printed to **container stdout** (not log files).
- Document `WEB_PASSWORD_RESET=true` to rotate from env when `web-auth.json` already exists; startup fails if `WEB_PASSWORD` disagrees without reset.
- Document `WEB_AUTH_TRUSTED_PROXIES` for reverse-proxy deployments (rate limit + `X-Forwarded-Proto` for Secure cookies).
- Document `WEB_AUTH_DISABLE_I_UNDERSTAND=true` when disabling auth on non-loopback binds.
- Document `audit-.log` under `DATA_DIR` (separate from operational logs).

## Edits outside strict ownership

- None intentional beyond `TelegramClient/FolderTemplateHelper.cs` (specified in T4 scope).

## Deferred findings

| ID | Reason |
|----|--------|
| F-07 (partial) | `ConfigStore`, `TelegramApp` session path, `SESSION_KEY` — out of T4 file ownership |
| F-05 (partial) | Forwarded-IP partitioning integration test and full audit-sink isolation from `/api/logs` — needs proxy test harness |
| F-06 (partial) | `__Host-` cookie prefix not adopted (spec allowed env + forwarded proto only) |
| F-04 (partial) | No SQLite session table (Option B); epoch in cookie + `web-auth.json` only |
| F-09+ | Out of T4 scope (WPF updater, CSRF, logs, etc.) |

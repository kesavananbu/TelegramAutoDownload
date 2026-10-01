# 04 — Security Findings: TelegramAutoDownload

**Scope:** commit `1c3753e`, branch `enterprise-readiness` (Composer 2.5 re-run). Read-only review;
no file outside `docs/security/` was modified; `dotnet build` not run.
**Note:** HEAD adds assessment docs and draft CI workflows; application source paths cited below
are unchanged from the prior Phase 4 pass.
**Companion document:** [`04-threat-model.md`](./04-threat-model.md) — read §1.1 first, it corrects
the "default deployment" assumption that drives the *Reachable by default* column below.

## Conventions

- **Verified** = I read the code path end to end and quote `path:line`. **Inferred** = reasoning
  beyond what the source shows; always labelled.
- **Exploitability verdict** is one of: *Proven attack path* (concrete preconditions → steps →
  impact), *Conditional* (needs a stated non-default toggle), or *Theoretical /
  defense-in-depth* (no reachable path found; fix for hygiene, not for a live threat). No CVSS is
  quoted for anything I could not reach.
- **Reachable by default** means reachable in *Baseline Deployment* (BD) as defined in the threat
  model §1.1: shipped `docker-compose.yml` on a shared Docker network behind a reverse proxy, with
  `admin`/`changeme` untouched. For the WPF product, "default" means a stock install running its
  startup update check.

## Counts

| Severity | Count | IDs |
|---|---|---|
| Critical | 3 | F-01, F-02, F-03 |
| High | 5 | F-04, F-05, F-06, F-07, F-08 |
| Medium | 8 | F-09, F-10, F-11, F-12, F-13, F-14, F-15, F-16 |
| Low | 8 | F-17, F-18, F-19, F-20, F-21, F-22, F-23, F-26 |
| Informational | 2 | F-24, F-25 |
| **Total** | **26** | |

**Reachable in a default deployment (13):** F-01, F-02, F-03, F-04, F-05, F-06, F-07, F-15, F-16,
F-17, F-18, F-19, F-20 — plus F-09, F-10 and F-22 for the WPF product, and F-21 as a standing
architectural property. **Misconfiguration-only or opt-in (4):** F-08, F-11, F-12, F-13.

Leads from the brief that I could **not** confirm, or that are **wrong**, are recorded in
[`04-threat-model.md` §7](./04-threat-model.md#7-leads-that-turned-out-to-be-wrong-or-overstated)
and summarised again at the end of this document. Notably: the log-file path-traversal defense
holds, no secret leakage into logs was found, SQL is fully parameterised, and the CSRF lead is
materially overstated.

---

## F-01 — Shipped default credentials, with a pre-authentication oracle that confirms they are active

- **Severity:** Critical
- **Exploitability:** Proven attack path
- **Reachable by default:** **Yes**
- **CWE:** CWE-1392 (Use of Default Credentials), CWE-204 (Observable Response Discrepancy)

### Evidence (verified)

- `docker-compose.yml:16-17` — `WEB_USERNAME: "admin"` / `WEB_PASSWORD: "changeme"` as literal
  shipped values.
- `WebAuthService.cs:16` — `private const string DefaultPassword = "changeme";`
- `WebAuthService.cs:207-217` — when no credentials file exists and `WEB_PASSWORD` is unset,
  username defaults to `"admin"` and password to `"changeme"`.
- `WebAuthService.cs:178-179` — `/api/web-auth/status` is on the pre-auth allowlist.
- `Program.cs:63-64` — that pre-auth endpoint returns `defaultPassword` and `mustChangePassword`
  straight from `auth.UsingDefaultPassword`.
- `WebAuthService.cs:69-71` — using the default only produces a `Log.Warning`; nothing is enforced.
- `wwwroot/index.html:26` — the login page itself renders "Default password **changeme** is active".

### Attack path

**Preconditions:** network reach to `:8080` (in BD: any peer on the Docker network, or anyone the
reverse proxy admits). No credential.

1. `GET /api/web-auth/status` → `{"enabled":true,"authenticated":false,"defaultPassword":true,...}`.
   The attacker now *knows*, without a single login attempt, that `admin`/`changeme` will work.
2. `POST /api/web-auth/login` `{"username":"admin","password":"changeme"}` → `200` and a
   `tad_web` cookie.
3. The dashboard forces a password-change modal client-side only (`app.js:99-101`). The server does
   **not** gate any endpoint on it — `WebAuthMiddleware.cs:15-19` checks only for a valid session.
   Verified: `Program.cs:157-533` has no `mustChangePassword` check. The attacker skips the UI and
   calls the API directly with full authority.

**Impact:** complete control of the dashboard, which owns asset A1 (the Telegram session). Chains
directly into F-02 for arbitrary file write and A1 exfiltration.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`, `Program.cs`, `docker-compose.yml`.

1. **Remove the default entirely.** Delete `DefaultPassword` (`WebAuthService.cs:16`). In
   `Load()`, if `WEB_PASSWORD` is unset and no `web-auth.json` exists, generate a 24-character
   CSPRNG password (`RandomNumberGenerator.GetString`), write it once to stdout with an explicit
   "change this now" banner, and require it for first login. Never accept a hardcoded fallback.
2. In `docker-compose.yml:16-17`, replace the literals with `WEB_PASSWORD: ${WEB_PASSWORD:?set
   WEB_PASSWORD in .env}` so compose refuses to start without an operator-supplied value.
3. **Stop leaking state pre-auth.** Reduce `/api/web-auth/status` to `{enabled}` when
   unauthenticated (`Program.cs:52-66`). Move `defaultPassword` / `mustChangePassword` into the
   authenticated response only.
4. **Enforce the password change server-side.** Add a check in `WebAuthMiddleware.InvokeAsync`: if
   `auth.UsingDefaultPassword` (or a new `MustChangePassword` flag) is true, reject every path
   except `/api/web-auth/change-password`, `/api/web-auth/logout`, and the static assets, with
   `403 {"error":"passwordChangeRequired"}`.

### Regression tests to write

- `Status_Unauthenticated_DoesNotRevealDefaultPasswordState` — `GET /api/web-auth/status` with no
  cookie; assert the response body contains neither `defaultPassword` nor `mustChangePassword`.
- `Load_NoPasswordConfigured_DoesNotAcceptChangeme` — clear `WEB_PASSWORD`, no credentials file;
  assert `ValidateCredentials("admin","changeme")` is `false`.
- `Middleware_WhenPasswordChangeRequired_Blocks403OnBusinessEndpoints` — valid session +
  `UsingDefaultPassword`; assert `POST /api/settings/download-folder` → `403`, and
  `POST /api/web-auth/change-password` → allowed.

---

## F-02 — Arbitrary filesystem write via unvalidated download root and per-chat folder template

- **Severity:** Critical
- **Exploitability:** Proven attack path
- **Reachable by default:** **Yes** (chained with F-01, requires no credential the attacker does not already have)
- **CWE:** CWE-22 (Path Traversal), CWE-73 (External Control of File Name or Path)

### Evidence (verified)

Two independent injection points, both accepting a raw client string with **zero** validation:

- `Program.cs:179-183` — `POST /api/settings/download-folder` does
  `host.UpdateSettings(c => { c.PathSaveFile = req.Folder; })`. No canonicalisation, no allowlist,
  no rooted-path rejection.
- `Program.cs:274` — `PATCH /api/chats/{id}` does `chat.FolderTemplate = patch.FolderTemplate`.
  Same: no validation.

Both flow into the same resolver:

- `FolderTemplateHelper.cs:40-63` (`Resolve`) — substitutes tokens, then for relative templates
  replaces only `Path.GetInvalidPathChars()`. **`..` is not in that set and survives verbatim.**
- `FolderTemplateHelper.cs:56-61` — the sanitising loop is explicitly skipped for rooted paths.
- `FolderTemplateHelper.cs:29-32` — the XML doc *documents absolute paths as a feature*:
  "Absolute path — the template starts with a drive letter or UNC root … Returned unchanged".
- `FolderTemplateHelper.cs:76-83` — `Path.IsPathRooted(resolvedTemplate) ? resolvedTemplate : …`,
  so an absolute template becomes the destination directory directly.
- `FolderTemplateHelper.cs:103-107` (`CombineRelative`) — splits on `/` and `\` and `Path.Combine`s
  the parts. `Path.Combine` does **not** normalise, so `..` segments are preserved and resolved by
  the filesystem.
- `BaseMessage.cs:264-271` — `ResolveDownloadDirectory(...)` then `Directory.CreateDirectory(fullDir)`
  and `Path.Combine(fullDir, fileName)`.
- `BaseMessage.cs:145` — `File.Move(partPath, finalPath, overwrite: true)`. **`overwrite: true`** —
  existing files are silently replaced.
- `DownloadOrchestrator.cs:109-111` — the orchestrator reads `cfg.PathSaveFile` as the root on every
  dispatch, so a changed root takes effect immediately with no restart.

### Attack path

**Preconditions:** one authenticated session (F-01 supplies it for free in BD); at least one
monitored chat that will receive any media. The attacker can also self-supply the content: they only
need to be able to post into a chat the operator monitors, or trigger
`POST /api/chats/{id}/bootstrap` (`Program.cs:415-416`) to replay existing history.

**Variant A — escape via `..`:**
1. `PATCH /api/chats/{id}` with `{"folderTemplate":"../../TelegramAutoDownload"}`.
2. `Resolve` leaves `..` intact; `CombineRelative` produces
   `/downloads/../../TelegramAutoDownload`, which the filesystem resolves to
   `/TelegramAutoDownload` — and with `DATA_DIR=/data` and `XDG_CONFIG_HOME=/data`
   (`Dockerfile:44-46`), a template of `../data/TelegramAutoDownload` lands exactly on the
   directory holding `session.dat` (`TelegramApp.cs:101-106`).
3. `POST /api/chats/{id}/bootstrap` → the next download's `File.Move(..., overwrite: true)` writes
   into that directory with an attacker-chosen file name.

**Variant B — absolute path, no traversal needed:**
`POST /api/settings/download-folder` with `{"folder":"/data"}`. Every subsequent download is written
under `/data` directly. Documented behaviour, not a bug in the resolver — which is why it must be
fixed at the API boundary.

**Impact, in order of severity:**
- **Overwrite `/data/.web-auth-secret`** (`WebAuthService.cs:237-245`) with content the attacker
  controls (they chose the Telegram file) → they can now mint valid session cookies for any
  username and any expiry (`WebAuthService.cs:131-146`). Permanent authentication control that
  survives password changes and restarts.
- **Overwrite `/data/web-auth.json`** (`WebAuthService.cs:220-229`) → set the stored password hash.
- **Exfiltrate A1:** point the download root at a directory the attacker can read by other means,
  or overwrite `config.json` to redirect state. Combined with the file-listing behaviour in
  `GetPathOfDuplicateFile` (`BaseMessage.cs:338`), the attacker also gains a directory-existence
  oracle.
- **Corrupt the host:** `/data` and `/downloads` are host bind mounts (`docker-compose.yml:25-28`),
  so writes land on the host filesystem.

This is the single finding that converts "someone logged into the dashboard" into "the Telegram
account is gone and stays gone".

### Remediation

**File:** `TelegramAutoDownload.Headless/Program.cs`, `TelegramClient/FolderTemplateHelper.cs`.

1. **Validate at the API boundary (`Program.cs:179-183`).** Add a shared helper:
   ```
   static bool TryResolveWithinRoot(string root, string candidate, out string resolved)
   ```
   Implement as: `Path.GetFullPath(Path.Combine(root, candidate))`, then require
   `resolved.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, OrdinalIgnoreCase)`.
   Reject otherwise with `400`.
2. **Constrain the download root to an allowlist.** `POST /api/settings/download-folder` must accept
   only a path under `HeadlessPaths.DownloadsDir` (or a new explicit `ALLOWED_DOWNLOAD_ROOTS` env
   list). Reject anything rooted elsewhere, anything containing `..` after normalisation, and any
   symlink whose target escapes the root (resolve with `new DirectoryInfo(p).ResolveLinkTarget(true)`).
3. **Make folder templates strictly relative (`Program.cs:274`).** Reject `Path.IsPathRooted`,
   reject any segment equal to `..` or `.`, reject `:` and NUL, and cap the depth (e.g. 8 segments)
   and total length (e.g. 200 chars). Do this in the endpoint **and** defensively in
   `FolderTemplateHelper.Resolve`.
4. **Remove the absolute-path feature.** Delete the `Path.IsPathRooted` branch at
   `FolderTemplateHelper.cs:76-83` and update the XML doc at `:29-32`. If an absolute destination is
   genuinely required, gate it behind an operator-only env var, never a web-settable field.
5. **Move secrets out of a writable download-reachable tree.** Even after the above, relocate
   `web-auth.json`, `.web-auth-secret`, `session.dat`, and `config.json` to a directory that is
   never a candidate download root, and mount it `:ro` where possible.

### Regression tests to write

- `DownloadFolder_RejectsAbsolutePathOutsideAllowedRoot` — `POST /api/settings/download-folder`
  with `/data`, `/etc`, `C:\Windows`; assert `400` and that `config.PathSaveFile` is unchanged.
- `DownloadFolder_RejectsTraversal` — `../`, `..\\`, `downloads/../../data`, URL-encoded variants.
- `FolderTemplate_RejectsDotDotSegment` — `PATCH /api/chats/{id}` with
  `{"folderTemplate":"../../x"}`; assert `400`.
- `FolderTemplate_RejectsRootedPath` — `/etc/cron.d`, `C:\Windows\Temp`, `\\\\server\\share`.
- `ResolveDownloadDirectory_NeverEscapesRoot` — property/fuzz test over templates built from
  `..`, `/`, `\`, `~`, NUL, and token strings; assert
  `Path.GetFullPath(result).StartsWith(Path.GetFullPath(root))` for every input.
- `Resolve_AbsoluteTemplate_IsRejectedNotPassedThrough` — pins the removal of the documented
  absolute-path behaviour so it cannot be reintroduced.

---

## F-03 — Dashboard password stored as bare unsalted SHA-256

- **Severity:** Critical
- **Exploitability:** Proven attack path (given any read of `web-auth.json`)
- **Reachable by default:** **Yes** (the weak hash is how every deployment stores the password)
- **CWE:** CWE-916 (Password Hash With Insufficient Computational Effort), CWE-759 (Missing Salt)

### Evidence (verified)

- `WebAuthService.cs:248-249`:
  ```
  private static byte[] HashPassword(string password) =>
      SHA256.HashData(Encoding.UTF8.GetBytes(password));
  ```
  One unsalted SHA-256 pass. No salt, no pepper, no KDF, no work factor, no versioning.
- `WebAuthService.cs:220-229` — the hash is Base64'd into `web-auth.json` with no salt field and no
  algorithm identifier, so there is no migration path baked in.
- `WebAuthService.cs:196-198` — read back and compared directly; `:116` and `:83` use it for
  verification.
- `WebAuthService.cs:102-105` — minimum length is 8 characters and there are no other composition
  or breach-list checks, so the plaintext space being protected is small.

### Attack path

**Preconditions:** read access to `web-auth.json`. Available to AC4 (host user — `/data` is bind
mounted, `docker-compose.yml:25`), AC5 (any process in the container), any backup or snapshot of the
volume, and to a remote attacker who first achieves F-02.

1. Read `PasswordHashBase64`.
2. Unsalted SHA-256 of an 8-character password is recovered by GPU brute force or a rainbow table in
   minutes to hours. There is no per-installation salt, so one table serves every deployment of this
   product, and identical passwords across deployments produce identical hashes.
3. The recovered plaintext is very likely reused elsewhere by the operator.

**Impact:** dashboard takeover, and a credential likely valid against other operator accounts.
Independent of F-02 because the same file is readable by everyone on the host.

**Why this is Critical rather than High:** the asset it guards is A1, and the hash is exposed to a
broader set of actors than the network attack surface — every host user and every volume backup.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`.

1. Replace `HashPassword` with **Argon2id** via `Konscious.Security.Cryptography.Argon2` with
   explicit, recorded parameters: **memory 64 MiB (65536 KiB), iterations 3, parallelism 2 (degree
   of parallelism), 128-bit random salt from `RandomNumberGenerator.GetBytes(16)`, 256-bit output.**
   These are the OWASP Password Storage Cheat Sheet Argon2id baseline.
   If adding a dependency is unacceptable, use the in-framework fallback: `Rfc2898DeriveBytes`
   with **HMACSHA256, 600,000 iterations, 128-bit random salt, 256-bit output** (OWASP PBKDF2-HMAC-SHA256
   guidance). Do not use a bare hash under any circumstance.
2. Change the on-disk format to a versioned, self-describing encoding:
   `{"version":2,"algorithm":"argon2id","m":65536,"t":3,"p":2,"salt":"<b64>","hash":"<b64>"}`.
   Keep `version:1` readable for one release: on a successful `version:1` login, transparently
   re-hash to `version:2` and rewrite the file.
3. Keep `CryptographicOperations.FixedTimeEquals` for the comparison (`:83`, `:116`) — that part is
   already correct.
4. Raise the minimum length at `:102` to 12 characters and reject the top-1000 breached-password
   list.
5. Write the file with restrictive permissions — see F-07.

### Regression tests to write

- `HashPassword_SameInputTwice_ProducesDifferentOutput` — proves a random salt is in use.
- `ValidateCredentials_AcceptsCorrect_RejectsWrong` — round-trip through save/load.
- `Argon2Parameters_MatchPolicy` — asserts m=65536, t=3, p=2 so a future refactor cannot silently
  weaken the work factor.
- `LoadCredentials_V1Sha256File_StillAuthenticates_ThenUpgradesToV2` — reads a fixture
  `web-auth.json` in the legacy shape, asserts login succeeds and the file is rewritten as
  `version:2`.
- `HashPassword_TakesAtLeast50ms` — a coarse floor that fails if the KDF is swapped for a fast hash.

---

## F-04 — Stateless session cookie cannot be revoked; logout and password change are both no-ops against it

- **Severity:** High
- **Exploitability:** Proven attack path
- **Reachable by default:** **Yes**
- **CWE:** CWE-613 (Insufficient Session Expiration), CWE-384 (Session Fixation family)

### Evidence (verified)

- `WebAuthService.cs:131-146` — the cookie is `username|expiry|HMAC(username|expiry)`. There is no
  session identifier, no nonce, and **no credential generation counter**.
- `WebAuthService.cs:151-171` — validation recomputes the HMAC and checks expiry. No server-side
  store is consulted, so the server has no way to say "not this one".
- `WebAuthService.cs:148-149` — `SignOut` only calls `response.Cookies.Delete`. It instructs the
  *browser* to drop the cookie. A copy already captured stays valid.
- `Program.cs:83-87` — the logout endpoint calls only `SignOut`; nothing server-side changes.
- `WebAuthService.cs:114-125` — `TryChangePassword` updates `_passwordHash` and writes the file. It
  does **not** touch `_signingKey`, so every cookie minted before the change still verifies.
- `WebAuthService.cs:237-245` — the signing key is persisted to `.web-auth-secret` and reused across
  restarts, so not even a container restart invalidates sessions.
- `WebAuthService.cs:59-61` — default lifetime 7 days, configurable up to 365.

### Attack path

**Preconditions:** one captured cookie value. Realistic capture routes: the plaintext hop in BD
(F-06), a same-site attacker (F-14), a shared browser, a proxy access log, or shoulder-surfing a
`curl` command.

1. Attacker captures `tad_web`.
2. Operator notices something wrong and does the two things a reasonable person would do: clicks
   **Sign out**, then changes the dashboard password.
3. Both are ineffective. The attacker replays the cookie and retains full access for the remainder
   of the 7-day window.
4. There is no way for the operator to fix this from the UI. The only effective remedy is deleting
   `/data/.web-auth-secret` and restarting the container — which is documented nowhere and would
   not occur to an operator.

**Impact:** an incident cannot be contained. The compromise window is fixed at up to 7 days
regardless of operator response. On its own this is High; it is what makes F-06 and F-14 matter.

**Note on the brief's question "check whether the HMAC covers everything it needs to":** the
signature construction itself is sound — the payload is re-canonicalised from the parsed `user` and
`long expires` before verification (`:165`), so there is no delimiter-splitting or truncation
ambiguity; HMAC-SHA256 is not length-extendable; and the comparison is constant-time
(`:257-262`). The defect is what the payload **omits**, not how it is signed.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`, plus a schema migration.

Pick one of two designs — do not half-implement both.

**Option A (minimal, keeps the stateless design).** Add a `CredentialGeneration` integer, persisted
in `web-auth.json` and incremented by `TryChangePassword` and by an explicit "sign out everywhere"
action. Put it inside the signed payload: `username|generation|expiry`. `TryGetSession` rejects any
cookie whose generation is below the current value. This makes password change revoke all sessions —
which is the behaviour operators already assume — but does **not** give per-session logout.

**Option B (recommended for enterprise).** Add a server-side session table to the existing SQLite
database (`Database.cs` migration `00N_sessions.sql`): `session_id TEXT PRIMARY KEY,
username TEXT, created_at TEXT, last_seen_at TEXT, expires_at TEXT, revoked_at TEXT,
user_agent TEXT, ip TEXT`. The cookie carries only a 256-bit CSPRNG opaque `session_id`
(`RandomNumberGenerator.GetBytes(32)`, Base64Url). `TryGetSession` looks the row up and rejects
revoked or expired rows. `SignOut` sets `revoked_at`. `TryChangePassword` revokes all rows for the
user. Add `GET /api/web-auth/sessions` and `DELETE /api/web-auth/sessions/{id}` so the operator can
see and kill sessions.

In both options, additionally:
- Reduce the default lifetime at `:59-61` from 7 days to **8 hours**, with a rolling idle timeout of
  30 minutes (Option B makes this easy via `last_seen_at`).
- Rotate the cookie value on privilege change (immediately after a successful password change).

### Regression tests to write

- `ChangePassword_InvalidatesExistingSessions` — sign in, capture the cookie, change the password,
  assert the old cookie now yields `401`.
- `SignOut_InvalidatesCookieServerSide` — sign in, capture, `POST /api/web-auth/logout`, replay the
  captured cookie directly (not via the browser jar), assert `401`.
- `Session_ExpiresAtConfiguredLifetime` — mint with a short lifetime, advance a clock abstraction,
  assert rejection.
- `Session_RevokedRow_IsRejected` (Option B) — revoke the row out of band, assert `401`.
- `Cookie_TamperedGenerationOrId_IsRejected` — flip bytes in each cookie segment, assert `401`.

---

## F-05 — No brute-force protection, no lockout, and no authentication logging whatsoever

- **Severity:** High
- **Exploitability:** Proven attack path
- **Reachable by default:** **Yes**
- **CWE:** CWE-307 (Improper Restriction of Excessive Authentication Attempts), CWE-778 (Insufficient Logging)

### Evidence (verified)

- `Program.cs:68-81` — the full login handler. On failure it returns `401` at `:76-78` and **logs
  nothing**. On success it calls `SignIn` at `:79` and **logs nothing**.
- `Program.cs:25-44` — the entire service/middleware configuration. There is no
  `AddRateLimiter`/`UseRateLimiter`, no `AddAntiforgery`, no throttling middleware. I grepped the
  file: no rate-limiting construct exists anywhere in the project.
- `WebAuthService.cs:77-85` — `ValidateCredentials` has no attempt counter, no delay, no
  lockout state.
- `WebAuthService.cs:88-129` — `TryChangePassword` likewise: unlimited attempts against the
  *current* password, and the only log line (`:127`) fires on success. Failures are silent.
- With F-03's fast hash, each attempt costs the server one SHA-256 — there is not even incidental
  rate limiting from the KDF.

### Attack path

**Preconditions:** network reach. No credential.

1. Script `POST /api/web-auth/login` in a loop. Nothing throttles, delays, or locks.
2. Because the password minimum is 8 characters (`WebAuthService.cs:102`) and the hash is a single
   SHA-256, server-side cost per guess is negligible — the only limit is bandwidth.
3. **Nothing is recorded.** No failed-login line, no successful-login line, no source IP. Post-incident
   it is impossible to establish whether, when, or from where a compromise occurred — and
   impossible to detect the attack while it is running.
4. `POST /api/web-auth/change-password` is a second unthrottled oracle for the *current* password,
   reachable with any valid session.

**Impact:** direct credential compromise for weak-but-not-default passwords, and — independently —
a complete absence of detection and forensic capability. The second half is the more serious
enterprise gap: even a correctly configured deployment is blind.

### Remediation

**File:** `TelegramAutoDownload.Headless/Program.cs`, `WebAuthService.cs`.

1. **Rate limit.** Add `builder.Services.AddRateLimiter(...)` with a fixed-window policy named
   `"login"`: **5 permits per 15-minute window, partitioned by client IP**
   (`RateLimitPartition.GetFixedWindowLimiter(httpContext.Connection.RemoteIpAddress)`),
   `QueueLimit = 0`, rejection status `429`. Apply with
   `.RequireRateLimiting("login")` to `/api/web-auth/login` and `/api/web-auth/change-password`.
   Because BD runs behind a reverse proxy, this requires `UseForwardedHeaders` with a configured
   `KnownProxies`/`KnownNetworks` — otherwise every request partitions to the proxy IP and the
   limiter becomes a global DoS switch. Do not skip that step.
2. **Progressive lockout.** In `WebAuthService`, track failures per username in memory: after 5
   consecutive failures, refuse for 15 minutes; reset on success. Return the same generic
   `401 "Invalid username or password"` during lockout so it is not an enumeration oracle.
3. **Log security events.** Add explicit structured events at `Program.cs:76` and `:79`:
   `Log.Warning("web-auth.login.failed user={User} ip={Ip}")` and
   `Log.Information("web-auth.login.success user={User} ip={Ip}")`. Add the same for
   change-password success/failure, logout, lockout-triggered, and every `401` emitted by
   `WebAuthMiddleware.cs:21-23`. Never log the submitted password.
4. Write these to a **separate audit sink** (`WriteTo.File("audit-.log")`) so that log-rotation of
   the operational log cannot destroy the security trail, and so the audit stream is not exposed
   through `/api/logs/*` (F-19).

### Regression tests to write

- `Login_SixthAttemptWithinWindow_Returns429` — five failures then a sixth; assert `429`.
- `Login_LockoutDoesNotRevealValidUsername` — compare status and body for a locked-out valid
  username vs a nonexistent one; assert identical.
- `Login_Failure_EmitsAuditEvent` — capture the Serilog sink; assert a `web-auth.login.failed` event
  with the username and IP, and assert the password string does not appear in any event.
- `Login_Success_EmitsAuditEvent`.
- `ChangePassword_IsRateLimited`.
- `ForwardedHeaders_ClientIpIsUsedForPartitioning` — two requests with different
  `X-Forwarded-For` from a known proxy must partition separately.

---

## F-06 — `Secure` cookie flag defaults off; no TLS enforcement, no HSTS

- **Severity:** High
- **Exploitability:** Proven attack path
- **Reachable by default:** **Yes**
- **CWE:** CWE-614 (Sensitive Cookie Without Secure Attribute), CWE-319 (Cleartext Transmission)

### Evidence (verified)

- `WebAuthService.cs:141`:
  ```
  Secure = string.Equals(Environment.GetEnvironmentVariable("WEB_AUTH_SECURE_COOKIES"), "true", ...)
  ```
  Opt-in. Unset → `false`. `docker-compose.yml` does not set it, so BD runs with `Secure=false`.
- `Program.cs:42` — `UseUrls(... ?? "http://0.0.0.0:8080")`. Plain HTTP.
- `Dockerfile:46-48` — `LISTEN_URL` and `ASPNETCORE_URLS` both `http://0.0.0.0:8080`.
- `Program.cs:44-49` — no `UseHttpsRedirection`, no `UseHsts`, no `CookiePolicy`. Verified across
  the whole file: neither appears.
- `WebAuthService.cs:143` — `MaxAge = _sessionLifetime`, so it is a persistent cookie (7 days by
  default), not a session cookie. It survives browser restarts and sits on disk.
- `WebAuthService.cs:139-140` — `HttpOnly = true` and `SameSite = Lax` are set correctly; those
  parts are fine.

### Attack path

**Preconditions:** ability to observe traffic on any hop that is not TLS-protected, or to induce a
plaintext request.

1. In BD the proxy terminates TLS and forwards plaintext to `:8080`. Any peer on that Docker
   network, and anything positioned on the proxy↔container hop, sees the cookie in the clear.
2. In Variant P (published port, which the commented-out `ports:` block in
   `docker-compose.yml:12-13` invites), the entire path is plaintext HTTP and the cookie crosses the
   LAN in the clear on every request.
3. Because `Secure` is absent, a browser will also send the cookie over `http://` to the same host
   even when the operator normally uses `https://` — so an attacker who can force one plaintext
   request (an `http://` image reference, a typed URL, a captive portal) harvests it.
4. Combined with F-04 the harvested cookie is good for up to 7 days and cannot be revoked.

**Impact:** session theft leading to full dashboard control and therefore A1.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`, `Program.cs`, `Dockerfile`.

1. **Invert the default.** At `WebAuthService.cs:141`, set `Secure = true` unless
   `WEB_AUTH_INSECURE_COOKIES=true` is explicitly set. Log a prominent `Log.Warning` when the
   insecure escape hatch is used, and refuse it when `ASPNETCORE_ENVIRONMENT=Production`.
2. Add `__Host-` prefix semantics: rename the cookie to `__Host-tad_web`, which browsers enforce as
   `Secure` + `Path=/` + no `Domain`. This makes the security property browser-enforced rather than
   configuration-dependent. Update `CookieName` at `:15` and the `Delete` call at `:148`.
3. Add `UseForwardedHeaders` (needed anyway for F-05) with `ForwardedHeaders.XForwardedProto` so the
   app knows the external scheme is HTTPS and `Secure` cookies work correctly behind the proxy.
4. Add `app.UseHsts()` with `max-age=31536000; includeSubDomains`, and document that TLS termination
   is mandatory. Optionally support `LISTEN_URL=https://...` with a mounted certificate for
   deployments without a proxy.
5. Change `MaxAge` (`:143`) to a session cookie plus a server-side expiry (Option B of F-04), so the
   credential is not persisted to the browser's disk.

### Regression tests to write

- `SignIn_SetsSecureCookieByDefault` — assert the `Set-Cookie` header contains `Secure`,
  `HttpOnly`, `SameSite=Lax`, and `Path=/`.
- `SignIn_InsecureOptOut_RequiresExplicitEnvAndLogsWarning`.
- `SignIn_InProductionEnvironment_IgnoresInsecureOptOut`.
- `CookieName_UsesHostPrefix` — pins `__Host-tad_web`.
- `ForwardedProto_Https_IsHonoured`.

---

## F-07 — Secrets at rest written with default permissions; session encryption key co-located with the session it protects

- **Severity:** High
- **Exploitability:** Proven attack path for the local/host actors; partly **[INFERRED]** for the encryption-key detail
- **Reachable by default:** **Yes**
- **CWE:** CWE-732 (Incorrect Permission Assignment for Critical Resource), CWE-312 (Cleartext Storage of Sensitive Information)

### Evidence (verified)

Four sensitive files, all in one directory, all written with no explicit mode:

| File | Written at | Contents |
|---|---|---|
| `session.dat` | `TelegramApp.cs:101-106` | Full Telegram MTProto session (A1) |
| `.web-auth-secret` | `WebAuthService.cs:237-245` | HMAC signing key for all sessions (A2) |
| `web-auth.json` | `WebAuthService.cs:220-229` | Password hash (A2) |
| `config.json` | `ConfigStore.cs:20-22, 41` | `ApiHash`, `BotToken` in plaintext (`ConfigParams.cs:12, 16`) |

- **`SetUnixFileMode` / `UnixFileMode` / `SetAccessControl` appear nowhere in the repository** — I
  grepped the whole tree. Every one of the four is created by `File.WriteAllText` / `File.Create`,
  which means the process umask decides. In a .NET container the effective mode is typically `0644`
  → **world-readable**.
- `HeadlessPaths.cs:37-41` — `Directory.CreateDirectory(DataDir)` with no mode either.
- `docker-compose.yml:25` — `/volume1/docker/telegram_downloader:/data`, so all four files also
  exist on the host with those modes, reachable by AC4.
- `docker-compose.yml:11` vs `Dockerfile` — the uid drop to `1029:100` exists only in compose; the
  image itself has no `USER` (see F-16), so a plain `docker run` writes these as root.
- `ConfigParams.cs:12, 16` — `ApiHash` and `BotToken` are plain `string` properties serialised
  verbatim by `ConfigStore.Save` (`ConfigStore.cs:41`). No encryption, no DPAPI, no secret store.
- `HeadlessPaths.cs:22` declares `SessionFile => Path.Combine(DataDir, "session.dat")`, but
  `TelegramApp.cs:101-106` builds its own path from `SpecialFolder.ApplicationData`. With
  `XDG_CONFIG_HOME=/data` (`Dockerfile:46`) the real location is
  `/data/TelegramAutoDownload/session.dat`, **not** `/data/session.dat`. `HeadlessPaths.SessionFile`
  is dead and misleading — anyone writing a backup or permissions script from it will protect the
  wrong path.

### Attack path

**Preconditions:** read access to `/data` or its host bind mount. Available to AC4 (any host user),
AC5 (any process in the container), any volume backup, snapshot, or misconfigured `docker cp`.

1. Read `session.dat` → **[INFERRED]** load it with any WTelegramClient build. WTelegramClient
   encrypts its session store, but the key is derived from `api_hash` unless an explicit
   `session_key` is supplied; no `session_key` is supplied anywhere in this codebase
   (`TelegramApp.cs:106` passes only `appId, apiHash, sessionPath`). `api_hash` sits in plaintext in
   `config.json` in the same directory tree. So the encryption is defeated by reading the adjacent
   file. **I could not read the WTelegramClient source in this environment — flag this for
   confirmation before relying on it either way; the remediation below is correct regardless.**
2. Read `.web-auth-secret` → mint arbitrary valid cookies (`WebAuthService.cs:131-146`). No
   password needed, no brute force, no expiry limit.
3. Read `web-auth.json` → crack the unsalted SHA-256 (F-03).
4. Read `config.json` → `BotToken` (control of the notification bot) and `ApiHash`.

**Impact:** total compromise of A1 and A2 from a read-only foothold. Note how little is needed: one
file read, no code execution.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`, `ConfigStore.cs`, `HeadlessPaths.cs`,
`TelegramClient/TelegramApp.cs`, `Dockerfile`.

1. **Set modes explicitly on every write.** Add a helper in `HeadlessPaths`:
   ```
   static void WriteSecret(string path, string contents)  // 0600 file, 0700 parent dir
   ```
   implemented with `File.WriteAllText` followed by
   `File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite)` guarded by
   `OperatingSystem.IsWindows()`. On Windows, apply a `FileSecurity` ACL granting only the current
   user. Route `WebAuthService.cs:228`, `:243`, and `ConfigStore.cs:21, 41` through it. Set the
   `DataDir` mode to `0700` at `HeadlessPaths.cs:38`.
2. **Break the key/ciphertext co-location.** Pass an explicit WTelegramClient `session_key` from a
   dedicated secret — a new `SESSION_KEY` env var or a Docker/Kubernetes secret mounted outside
   `/data` — so `session.dat` is not decryptable from the data volume alone. This is the single
   highest-value change in this finding.
3. **Stop persisting `ApiHash` and `BotToken` in `config.json`.** Read them from env or a mounted
   secret and keep them out of the serialised config. If they must be persisted, encrypt with a key
   held outside `/data`. Add a `[JsonIgnore]`-equivalent so a future `POST /api/settings/credentials`
   does not rewrite them to disk in the clear.
4. **Delete or fix `HeadlessPaths.SessionFile`** (`HeadlessPaths.cs:22`) so it matches the real
   location, and have `TelegramApp` take the path via constructor injection instead of computing it
   from `SpecialFolder.ApplicationData`.
5. Add `USER` to the `Dockerfile` (F-16) and `chown`/`chmod 0700` the data directory at entrypoint.

### Regression tests to write

- `SaveCredentials_FileModeIs0600` — skip on Windows; assert
  `File.GetUnixFileMode(path) == (UserRead|UserWrite)`.
- `LoadOrCreateSigningKey_FileModeIs0600`.
- `ConfigStore_Save_FileModeIs0600`.
- `HeadlessPaths_DataDirMode_Is0700`.
- `ConfigStore_Save_DoesNotSerialiseApiHashOrBotToken` — assert neither key appears in the written
  JSON.
- `HeadlessPaths_SessionFile_MatchesTelegramAppPath` — pins the two against each other so they
  cannot drift again.

---

## F-08 — Authentication can be disabled wholesale by a single environment variable

- **Severity:** High
- **Exploitability:** Conditional (misconfiguration)
- **Reachable by default:** **No** — requires the operator to set `WEB_AUTH_ENABLED=false`
- **CWE:** CWE-1188 (Insecure Default/Initialization), CWE-306 (Missing Authentication)

### Evidence (verified)

- `WebAuthService.cs:50-57` — `WEB_AUTH_ENABLED=false` returns a service with `Enabled=false`, an
  empty signing key (`[]`), an empty username, and an empty hash.
- `WebAuthMiddleware.cs:9-13` — `if (!auth.Enabled || IsPublicPath(...))` → `await next(ctx)`.
  Every endpoint becomes anonymous.
- `WebAuthService.cs:78` — `ValidateCredentials` returns `true` unconditionally.
- `WebAuthService.cs:153` — `TryGetSession` returns `true` with `username = null`.
- `Program.cs:53-55` — the status endpoint reports `authenticated:true` to anyone.
- `Program.cs:70-71` — login returns `{ok:true, disabled:true}` to anyone.
- The only guardrail is an informational log line at `WebAuthService.cs:55`. Not a warning, not a
  startup refusal.

### Attack path

**Preconditions:** the flag is set. Realistic, because there is no documentation (the README never
mentions the headless server at all) and the natural debugging move when the default password is
rejected or forgotten is to turn auth off "temporarily".

1. Operator sets `WEB_AUTH_ENABLED=false` to troubleshoot and does not revert it.
2. Every endpoint in `Program.cs:120-533` is anonymous — including `POST /api/login/phone`,
   `POST /api/login/code`, `POST /api/login/password` (a remote attacker can drive a *new* Telegram
   login), `POST /api/settings/download-folder` (F-02, now pre-auth), and `/api/logs/stream` (F-19).
3. `POST /api/login/logout` (`Program.cs:150-154`) deletes `session.dat` (`TelegramApp.cs:149`),
   forcing full SMS re-verification — an anonymous destructive action.

**Impact:** the entire finding set collapses to pre-authentication. Downgraded from Critical only
because it is not the shipped default.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`, `Program.cs`.

1. Refuse to start with authentication disabled unless **both** `WEB_AUTH_ENABLED=false` **and**
   `ASPNETCORE_ENVIRONMENT=Development` are set. In any other environment, throw at
   `WebAuthService.Load()` with a message naming the variable.
2. When it is legitimately disabled in development, bind only to loopback: override
   `Program.cs:42` to `http://127.0.0.1:8080` so a disabled gate is never network-reachable.
3. Escalate `WebAuthService.cs:55` from `Log.Information` to `Log.Fatal`-level visibility and emit
   the warning on **every** request via middleware, plus a persistent red banner in the dashboard.
4. Add a `GET /healthz` that reports `authEnabled` so monitoring can alarm on it.

### Regression tests to write

- `Load_AuthDisabledInProduction_Throws`.
- `Load_AuthDisabledInDevelopment_Succeeds_AndBindsLoopbackOnly`.
- `Middleware_AuthDisabled_EmitsWarningPerRequest`.

---

## F-09 — WPF self-updater executes a downloaded installer with no integrity verification, and interpolates remote-controlled strings into a batch script run by `cmd.exe`

- **Severity:** Medium (mechanism is severe; the precondition is a compromised release channel)
- **Exploitability:** Proven attack path *given* AC6; the local-TOCTOU sub-issue is Theoretical / defense-in-depth
- **Reachable by default:** **Yes for the WPF product** — the update check runs on every startup (`README.md:46`)
- **CWE:** CWE-494 (Download of Code Without Integrity Check), CWE-78 (Argument Injection), CWE-22

### Evidence (verified)

- `AutoUpdateService.cs:24` — release metadata from `https://api.github.com/.../releases/latest`.
- `AutoUpdateService.cs:78-85` — `AssetName` and `browser_download_url` taken verbatim from the JSON.
  The URL is **not validated against the expected `github.com` host**.
- `AutoUpdateService.cs:140-158` — the asset is downloaded to disk. There is **no hash check, no
  Authenticode signature verification, no certificate pinning, no expected-size check**.
- `AutoUpdateService.cs:176` — the installer is then executed with
  `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS`.
- `AutoUpdateService.cs:130` — `Path.Combine(Path.GetTempPath(), release.AssetName)`. `AssetName` is
  remote-controlled and unsanitised: a name containing `..\` escapes the temp directory.
- `AutoUpdateService.cs:167-177` — a `.bat` is generated by string interpolation and
  `AutoUpdateService.cs:178-182` runs it via `cmd.exe /c`. `{tmpFile}` — which embeds the remote
  `AssetName` — is interpolated inside `""..."" `. An asset name containing a double quote breaks
  out of the quoting and appends arbitrary batch commands. Same pattern for the ZIP path at
  `:212-221` with `{srcDir}`, `{appDir}`, `{exeName}`.
- `AutoUpdateService.cs:249-251` — `IsNewer` falls back to `string.Compare` when `Version.TryParse`
  fails, so a non-numeric tag can be made to compare greater.

### Attack path

**Preconditions:** AC6 — control of the GitHub release metadata (maintainer account or token
compromise, a malicious release asset, or a successful MITM of `api.github.com` against a host with
a poisoned trust store).

1. Publish a release with a version that compares greater and an asset named, for example,
   `x" & powershell -enc <payload> & "y_Setup.exe`.
2. On the victim's next launch, `CheckAsync` accepts it (`:66`, `:81-82`), `DownloadAndInstallAsync`
   writes it to a path containing that name (`:130`), and the generated batch (`:167-177`) is
   executed by `cmd.exe` with the injected command intact.
3. Alternatively, skip the injection: the plain `.exe` is executed silently at `:176` with **no
   signature check**, so simply replacing the asset achieves execution. The injection only matters
   as a second, quieter route.
4. Because the app is normally launched by the interactive user and the installer requests
   elevation, a user who approves the UAC prompt (which they expect from an update) grants
   administrative execution.

**Impact:** code execution on every desktop install, at the privilege of whoever approves the
prompt. This is the highest-impact finding for product B.

**Explicit downgrade — the TOCTOU sub-issue.** The brief flagged the `.bat` path as privilege
escalation. `Path.GetTempPath()` resolves to the per-user `%LOCALAPPDATA%\Temp`, which is ACL'd to
that user, and the batch is written and launched by that same user. **No privilege boundary is
crossed on a default Windows profile, so I am downgrading this to Theoretical /
defense-in-depth.** It becomes real only if `TEMP`/`TMP` is redirected to a directory writable by
other principals (e.g. a machine-wide `C:\Temp`), which is a host misconfiguration, not a defect
here. Fix it anyway per step 4 below — it is cheap.

### Remediation

**File:** `TelegramAutoDownload/Services/AutoUpdateService.cs`.

1. **Verify before executing.** Publish a detached signature or a `SHA256SUMS` file with each
   release and verify it before running anything. Preferred: verify the **Authenticode signature**
   of the downloaded `.exe` with `X509Certificate.CreateFromSignedFile(tmpFile)` and assert the
   subject/thumbprint matches a pinned publisher certificate. Do both if possible. Refuse and delete
   the file on mismatch.
2. **Validate the download URL.** Assert `new Uri(assetUrl).Host` is exactly
   `github.com` or `objects.githubusercontent.com` and the scheme is `https`, before `GetAsync`
   at `:140`.
3. **Sanitise `AssetName`.** At `:130`, use
   `Path.GetFileName(release.AssetName)` and additionally reject anything not matching
   `^[A-Za-z0-9._-]+$`. Better: ignore the remote name entirely and write to a locally generated
   name such as `tad_update_{guid}.exe`.
4. **Stop generating batch scripts.** Replace both `.bat` paths (`:167-182`, `:212-226`) with a
   small signed helper executable shipped in the install directory, invoked with
   `ProcessStartInfo.ArgumentList` (which never goes through a shell). If a script must remain,
   write it to a freshly created directory with a random name and a restrictive ACL, and pass every
   variable via environment variables rather than interpolation.
5. **Fix version comparison.** At `:249-251`, reject any tag that does not parse as a `Version`
   instead of falling back to `string.Compare`.

### Regression tests to write

- `DownloadAndInstall_UnsignedAsset_Throws_AndDoesNotExecute` — inject a fake unsigned file; assert
  no process is started.
- `DownloadAndInstall_HashMismatch_Throws_AndDeletesTempFile`.
- `CheckAsync_RejectsNonGitHubAssetUrl` — `https://evil.example/x_Setup.exe`.
- `AssetName_WithQuotesOrTraversal_IsRejectedOrNeutralised` — `..\\..\\evil.exe`,
  `a" & calc & ".exe`; assert the resolved temp path stays inside `GetTempPath()` and contains no
  quote characters.
- `IsNewer_NonSemverTag_ReturnsFalse`.
- `UpdateLauncher_UsesArgumentList_NotShellString` — asserts no `cmd.exe` invocation remains.

---

## F-10 — `yt-dlp` and `ffmpeg` are downloaded from the internet and executed with no integrity verification

- **Severity:** Medium
- **Exploitability:** Proven attack path given control of the release metadata; otherwise Theoretical
- **Reachable by default:** **Yes for the WPF product.** **No for the container** — the Docker image installs yt-dlp via apt (`Dockerfile:34-37`), so this code path is not reached there
- **CWE:** CWE-494

### Evidence (verified)

- `YtdlpService.cs:19-24` — hardcoded GitHub API URL plus a hardcoded ffmpeg ZIP URL.
- `YtdlpService.cs:69-74` — `downloadUrl` is read from `browser_download_url` in the API response.
  Not validated against an expected host.
- `YtdlpService.cs:82-89` — if `yt-dlp.exe` is missing, the URL is fetched and written to
  `ExePath`. **No hash, no signature.**
- `YtdlpService.cs:179-188` (`DownloadFileAsync`) — `GetByteArrayAsync` → `WriteAllBytesAsync` →
  `File.Move(..., overwrite: true)`. No verification step exists in the method at all.
- `YtdlpService.cs:194-200` — the binary is then executed (`--version`), and the plugins execute it
  for every download (`YouTubePlugin.cs:29, 78`, `SocialMediaPlugin.cs:65, 117`).
- `YtdlpService.cs:145-162` — the ffmpeg ZIP is extracted by matching any entry ending in
  `/ffmpeg.exe` and writing it to the tools directory, again unverified.
- `YtdlpService.cs:96` — update decision uses `string.Compare` on version strings, not a date or
  version comparison. Verified: this makes the update logic fragile, though not directly exploitable.
- `ExePath` resolves under `%APPDATA%\TelegramAutoDownload\tools\` (`YtdlpService.cs:27-30` →
  `AppPaths.ToolsDir`), a user-writable directory. `YouTubePlugin.cs:176-179` and
  `SocialMediaPlugin.cs:247-250` prefer that copy over the install directory.

### Attack path

**Preconditions:** either AC6 (control of the yt-dlp release metadata / a MITM with a trusted cert),
or local write access to `%APPDATA%\TelegramAutoDownload\tools\`.

1. **Remote variant:** serve a modified `browser_download_url`. The referenced file is written to
   `yt-dlp.exe` and executed on the next download and on the next `--version` probe.
2. **Local variant:** any process running as the user drops its own `yt-dlp.exe` into the tools
   directory. Because the plugins prefer that path, it is executed. **No privilege boundary is
   crossed** — same user, so this is a persistence mechanism rather than an escalation. Downgraded
   accordingly.

**Impact:** code execution in the context of the desktop app.

### Remediation

**File:** `TelegramAutoDownload/Services/YtdlpService.cs`.

1. Pin an expected SHA-256 for each tool version and verify after download, before `File.Move`
   (`:187`). yt-dlp publishes `SHA2-256SUMS` with every release — fetch and check it, and verify its
   accompanying signature if present.
2. Verify the Authenticode signature of `yt-dlp.exe` and `ffmpeg.exe` before first execution;
   quarantine and refuse on failure.
3. Validate `downloadUrl` host (`:72-74`) against `github.com` / `objects.githubusercontent.com`
   over `https` only.
4. Prefer the install-directory copy over the user-writable `%APPDATA%` copy — invert the order at
   `YouTubePlugin.cs:176-182` and `SocialMediaPlugin.cs:247-253` — and set a restrictive ACL on the
   tools directory.
5. Replace the `string.Compare` version check at `:96` with a date/version-aware comparison, and
   refuse to *downgrade*.
6. For the container, pin the apt package version at `Dockerfile:36`
   (`yt-dlp=<version> ffmpeg=<version>`) so the image is reproducible (see F-16).

### Regression tests to write

- `EnsureYtdlp_HashMismatch_DoesNotReplaceBinary` — assert the existing file is untouched and no
  `.tmp` remains.
- `EnsureYtdlp_RejectsNonGitHubUrl`.
- `ResolveYtdlpPath_PrefersInstallDirOverAppData` — create both; assert the install-dir path wins.
- `EnsureFfmpeg_ZipEntryOutsideExpectedPath_IsIgnored` — zip-slip guard on the
  `EndsWith("/ffmpeg.exe")` match at `:154-155`.
- `IsNewer_DoesNotAllowDowngrade`.

---

## F-11 — Torrent engine opens the host to the swarm: UPnP port forwarding, DHT, LPD, and unbounded content size

- **Severity:** Medium
- **Exploitability:** Proven attack path for the disk/exposure impact; Conditional on the plugin being enabled
- **Reachable by default:** **No** — plugins are opt-in per chat. `MessageTextFactoryService.cs:145` treats a missing key as disabled ("Missing key = disabled. User must explicitly enable each plugin per chat")
- **CWE:** CWE-1327 (Binding to an Unrestricted IP Address), CWE-400 (Uncontrolled Resource Consumption), CWE-200

### Evidence (verified)

- `TorrentDownloadService.cs:273-280` — engine settings:
  `AllowPortForwarding = true`, `AllowLocalPeerDiscovery = true`, `AutoSaveLoadMagnetLinkMetadata = true`,
  `AutoSaveLoadDhtCache = true`. **No `ListenEndPoints` is configured**, so MonoTorrent's defaults
  bind on all interfaces.
- `TorrentDownloadService.cs:105-110` — per-torrent settings: `AllowDht = true`,
  `AllowPeerExchange = true`, `CreateContainingDirectory = true`.
- `TorrentDownloadService.cs:116, 121` — the magnet link or `.torrent` is added and started
  (`:124`) with **no size check at any point**. `knownTotalBytes` (`:99, 115, 160-161`) is only ever
  read for progress reporting; it is never compared against a limit.
- `TorrentPlugin.cs:13-19` — `CanHandle` accepts any magnet link found in the message text, or any
  local `.torrent` file.
- `MessageTextFactoryService.cs:100-110` — the magnet is extracted from each line of the Telegram
  message, i.e. from AC3-controlled input.
- `TorrentDownloadService.cs:266-271` — engine cache under `SpecialFolder.ApplicationData`, which is
  `/data` in the container (`Dockerfile:46`) — so DHT cache and magnet metadata are written into the
  secrets directory.
- `TorrentDownloadService.cs:189, 201-202` — the only limits are *inactivity* timeouts (30 minutes
  for metadata, 10 minutes for zero-progress). A torrent that is actively transferring has **no
  time, size, or bandwidth ceiling**.

### Attack path

**Preconditions:** the operator has enabled the Torrent plugin for at least one chat. AC3 can then
post to that chat with no application credential.

1. **Disk amplification:** post a magnet link for a multi-terabyte torrent. MonoTorrent downloads it
   with no cap, writing into the `/downloads` host bind mount (`docker-compose.yml:26`) until the
   host volume is full. The SQLite queue and the log directory share the host and fail alongside it.
   This requires nothing but a message.
2. **Inbound exposure:** `AllowPortForwarding = true` makes the engine attempt UPnP/NAT-PMP against
   the local router *from inside the network*, creating an inbound mapping to the container host —
   a firewall hole punched without operator action or visibility.
3. **Information disclosure:** DHT and LPD announce the host's IP together with the infohashes it is
   fetching. Any swarm observer learns the operator's IP and exactly what they are downloading. LPD
   additionally broadcasts on the local network, disclosing the activity to LAN neighbours.
4. **Legal/reputational:** the host becomes a seeding participant (`:139-140` breaks on
   `TorrentState.Seeding`, so it does reach seeding state) for arbitrary attacker-chosen content.

### Remediation

**File:** `BasePlugin/TorrentDownloadService.cs`, plus a new config surface.

1. **Default to closed.** Set `AllowPortForwarding = false` and `AllowLocalPeerDiscovery = false` at
   `:275-276`. Make each an explicit opt-in config flag with the risk documented.
2. **Bind explicitly.** Set `ListenEndPoints` to a configured address/port so the operator controls
   the exposure rather than inheriting a default.
3. **Enforce a size cap.** Add `MaxTorrentSizeBytes` (default 10 GiB) to `ConfigParams`. Check it in
   two places: after `Torrent.LoadAsync` at `:98-99` and `:114-115`, and again inside the poll loop
   when magnet metadata first arrives (`:154-158`, where `HasMetadata` flips) — abort with
   `StopAsync` + `RemoveAsync` if exceeded. The second check is the important one, because a magnet
   link's size is unknown until metadata arrives.
4. **Enforce a free-space floor** before `AddAsync` (`:116`, `:121`) using
   `new DriveInfo(outputFolder).AvailableFreeSpace`, requiring the torrent size plus a 10% margin.
5. **Add a wall-clock ceiling** alongside the inactivity timeouts at `:189, 201` (e.g. abort after
   24 hours regardless of progress).
6. **Stop seeding immediately on completion** and set an upload-slot limit of 0 unless seeding is
   explicitly enabled.
7. Move the engine cache (`:266-271`) out of `DataDir` into a dedicated cache directory.

### Regression tests to write

- `EngineSettings_PortForwardingAndLpd_DefaultToFalse` — pins the defaults.
- `Download_TorrentLargerThanCap_IsRejectedBeforeAnyDiskWrite` — fixture `.torrent` over the cap.
- `Download_MagnetMetadataOverCap_AbortsAfterMetadataArrives` — the magnet-specific path.
- `Download_InsufficientFreeSpace_IsRejected` — inject a free-space provider.
- `Download_ExceedsWallClockCeiling_IsAborted`.

---

## F-12 — SSRF: `YouTubePlugin.CanHandle` matches on a raw string prefix instead of the URL host

- **Severity:** Medium
- **Exploitability:** Proven attack path (blind SSRF); Conditional on the plugin being enabled
- **Reachable by default:** **No** — the YouTube plugin must be enabled for the chat (`MessageTextFactoryService.cs:145`)
- **CWE:** CWE-918 (SSRF), CWE-20 (Improper Input Validation)

### Evidence (verified)

- `YouTubePlugin.cs:20-23`:
  ```
  public override bool CanHandle(Config config)
  {
      return config.Text.StartsWith("https://youtu") || config.Text.StartsWith("https://www.youtu");
  }
  ```
  A prefix test on the raw string. `https://youtu.attacker.example/x`,
  `https://youtube.com.attacker.example/x`, and `https://youtu@internal.host/x` all satisfy it.
- `MessageTextFactoryService.cs:100-123` — `config.Text` is one line of the Telegram message, i.e.
  fully AC3-controlled.
- `YouTubePlugin.cs:122` — `await ytdlp.DownloadAsync(config.Text, cancelToken)`, so that string is
  handed to yt-dlp as the URL to fetch.
- **Contrast — the correct implementation exists in the same codebase:**
  `SocialMediaPlugin.cs:44-59` parses with `Uri.TryCreate`, extracts `uri.Host`, lowercases, strips
  `www.`, and matches against an allowlist with a proper `host == d || host.EndsWith("." + d)`
  suffix check. `YouTubePlugin` simply does not do this.
- Plugin ordering (`MessageTextFactoryService.cs:112-117`, priorities at `SocialMediaPlugin.cs:11`
  = 2 and `YouTubePlugin.cs:18` = 10) means SocialMedia is consulted first and correctly declines a
  non-allowlisted host — after which YouTubePlugin accepts it.

### Attack path

**Preconditions:** the YouTube plugin enabled for a chat AC3 can post to (any public channel the
operator monitors qualifies). No application credential.

1. Post `https://youtu.attacker.example/pivot`.
2. SocialMedia declines (host not allowlisted). Torrent declines. YouTube accepts via the prefix
   test.
3. yt-dlp fetches the attacker's URL from inside the deployment's network position, following
   redirects. The attacker can 302 to `http://169.254.169.254/latest/meta-data/`, to
   `http://127.0.0.1:8080/api/...`, or to any internal host, and use response timing and the
   resulting error text as an oracle.
4. **[INFERRED]** Whether yt-dlp's generic extractor follows a given redirect to a non-HTTP scheme
   or internal address depends on the yt-dlp version; I did not test a live instance. The
   request-forgery primitive itself is verified from this codebase.

**Why this is Medium and not High:** the response body is not returned to the attacker. Errors land
in the log (`SocialMediaPlugin.cs:159-166`, `YouTubePlugin.cs:113-120`) which the attacker cannot
read without a dashboard session. So it is a **blind** SSRF plus an outbound-fetch primitive, and it
requires an opt-in plugin.

### Remediation

**File:** `YoutubePlugin/YouTubePlugin.cs`.

1. Rewrite `CanHandle` (`:20-23`) to mirror `SocialMediaPlugin.cs:44-59` exactly: `Uri.TryCreate`
   absolute, require `https`, extract `Host`, lowercase, strip `www.`, then match an explicit
   allowlist `{"youtube.com", "youtu.be", "m.youtube.com", "music.youtube.com"}` with
   `host == d || host.EndsWith("." + d)`.
2. Extract the host-allowlist logic into one shared helper in `BasePlugin` and have **both** plugins
   call it, so the two implementations cannot drift again.
3. Add a defense-in-depth egress guard before invoking yt-dlp: resolve the host and reject
   loopback, link-local (`169.254.0.0/16`), and RFC1918 addresses; re-check after redirects by
   passing `--no-playlist` plus an explicit proxy/allowlist, or by pre-resolving and pinning.
4. Document that URL-fetching plugins should run with egress restricted at the network layer.

### Regression tests to write

- `CanHandle_RejectsLookalikeHosts` — `https://youtu.evil.com/x`,
  `https://youtube.com.evil.com/x`, `https://youtu@evil.com/x`, `https://notyoutube.com/x`.
- `CanHandle_AcceptsGenuineYouTubeHosts` — `https://youtu.be/abc`,
  `https://www.youtube.com/watch?v=abc`, `https://m.youtube.com/watch?v=abc`.
- `CanHandle_RejectsNonHttpsScheme`.
- `HostAllowlist_SharedHelper_IsUsedByBothPlugins` — reflection or an architecture test pinning that
  neither plugin does its own `StartsWith` matching.

---

## F-13 — User-supplied regex evaluated with no match timeout and no validation

- **Severity:** Medium
- **Exploitability:** Proven attack path for the crash; ReDoS is Conditional
- **Reachable by default:** **No** — requires a pattern to be configured
- **CWE:** CWE-1333 (Inefficient Regular Expression Complexity), CWE-20

### Evidence (verified)

Six evaluation sites, none with a timeout:

- `BaseMessage.cs:291-294` — `Regex regex = new(regexPattern);` then `regex.IsMatch(document.Filename)`.
  **No timeout, no try/catch, and the pattern is recompiled on every message.**
- `TelegramApp.cs:326-330` — `Regex.IsMatch(..., RegexOptions.IgnoreCase)`, no timeout.
- `TelegramApp.cs:583-588`, `TelegramApp.cs:877-883`, `TelegramApp.cs:1210-1215` — same.
- `FactoryMessagesService.cs:181-184` — same.
- **No global default is set either:** `AppContext.SetSwitch`/`REGEX_DEFAULT_MATCH_TIMEOUT` and
  `MatchTimeout` produce **zero** matches across the entire repository.
- The pattern arrives unvalidated from the API: `Program.cs:275-276` splits `patch.Filter` on `;`
  and assigns it straight to `chat.IgnoreFileByRegex`. There is no `try { new Regex(p) }` validation
  anywhere in the headless path.
- **The correct pattern exists in the WPF UI and was not carried over:**
  `FilterDialog.xaml.cs:40` uses `new Regex(Pattern, RegexOptions.None, TimeSpan.FromSeconds(1))`
  and `:204-205` uses `Regex.IsMatch(..., TimeSpan.FromSeconds(1))`. So the desktop dialog validates
  with a timeout, and the engine that actually runs the pattern does not.
- `ChatDto.cs:52` — `IgnoreFileByRegex` is a plain `List<string>` with no constraints.

### Attack path

**Two distinct issues.**

**(a) Unhandled-exception denial of service — Proven.**
Preconditions: one authenticated session.
1. `PATCH /api/chats/{id}` with `{"filter":"("}` → accepted, persisted (`Program.cs:275`), no
   validation.
2. The next media message in that chat reaches `BaseMessage.cs:293`, where `new Regex("(")` throws
   `ArgumentException` inside `CheckDownloadPolicy`, which has no guard.
3. The exception surfaces in `DownloadOrchestrator.DispatchAsync` (`DownloadOrchestrator.cs:176-185`),
   which marks the row `Failed` and continues — so the queue drains into permanent failure for that
   chat rather than crashing the process. A silent, persistent functional denial of service that the
   operator will struggle to diagnose, since the only signal is a `Log.Warning`.
   `document.Filename` being null would likewise throw `ArgumentNullException` at `:294`.

**(b) ReDoS — Conditional, and the realistic variant is the *benign* one.**
An authenticated attacker can set `(a+)+$` and then post a matching filename, pinning a download
thread's CPU indefinitely (no timeout exists to break the match). But the more likely real-world
occurrence needs no attacker at all: an operator writes a nested-quantifier pattern by hand —
the README actively encourages hand-written regex (`README.md:195-217`) — and AC3 supplies a
filename that triggers catastrophic backtracking. With `DownloadThreads` defaulting to 3
(`ConfigParams.cs:18`), three such matches stall the entire download pipeline.

### Remediation

**File:** `TelegramClient/Factory/Base/BaseMessage.cs`, `TelegramApp.cs`,
`Factory/Service/FactoryMessagesService.cs`, `TelegramAutoDownload.Headless/Program.cs`.

1. **Validate on input.** In `Program.cs:275-276`, compile each pattern inside
   `try { new Regex(p, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }` and return
   `400 {"error":"Invalid regex pattern: <p>"}` on failure. Also cap the pattern count (e.g. 20) and
   each pattern's length (e.g. 200 chars).
2. **Always pass a timeout.** Replace every call site listed above with
   `RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)` and catch
   `RegexMatchTimeoutException`, treating a timeout as "no match" plus a `Log.Warning` naming the
   pattern. As a belt-and-braces backstop, set the process-wide default once at startup:
   `AppContext.SetSwitch`-equivalent via
   `AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(200))`
   in `Program.cs` before the host is built.
3. **Compile once, cache.** `BaseMessage.cs:291-294` recompiles per message. Build a
   `Regex[]` per chat when the config changes (in `UpdateConfig`) and reuse it. This fixes a real
   throughput problem as well as the security issue.
4. **Guard the null filename** at `:294` with `document.Filename ?? string.Empty`.
5. Consider `RegexOptions.NonBacktracking` where the pattern syntax permits it — it makes
   catastrophic backtracking structurally impossible.

### Regression tests to write

- `PatchChat_InvalidRegex_Returns400_AndDoesNotPersist` — `"("`, `"[z-a]"`, `"(?<"`.
- `PatchChat_TooManyOrTooLongPatterns_Returns400`.
- `CheckDownloadPolicy_CatastrophicPattern_TimesOutWithinBudget` — pattern `(a+)+$`, subject
  `"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!"`; assert completion under 500 ms and that it is treated as no
  match.
- `CheckDownloadPolicy_NullFilename_DoesNotThrow`.
- `CheckDownloadPolicy_InvalidStoredPattern_DoesNotThrow` — simulates a config file written before
  validation existed.

---

## F-14 — No CSRF defense exists; the classic cross-site attack is nevertheless blocked (lead materially overstated)

- **Severity:** Medium
- **Exploitability:** Conditional — requires a same-site attacker (AC8)
- **Reachable by default:** **Partially** — the missing control is default, but exploitation needs an additional same-site foothold
- **CWE:** CWE-352 (CSRF)

### What the code actually shows (verified)

The brief states "No CSRF protection exists on any state-changing endpoint, and every one of them is
cookie-authenticated." The first half is correct; the conclusion usually drawn from it is not.

**Confirmed missing:**
- No `AddAntiforgery` / `ValidateAntiForgeryToken` anywhere in `Program.cs:25-44`.
- No `Origin` or `Referer` validation in `WebAuthMiddleware.cs` or any handler.
- No required custom header (the client sends only `Content-Type`, `app.js:132-136`).
- No CORS policy declared at all.

**Confirmed mitigating, by accident rather than design:**
- `WebAuthService.cs:140` — `SameSite = SameSiteMode.Lax`. Browsers do not attach a `Lax` cookie to
  cross-site `POST`, `PATCH`, or `DELETE`. A classic auto-submitting form attack therefore arrives
  with no cookie and gets `401`.
- Every state-changing handler binds a JSON body (`Program.cs:68, 88, 171, 179, 185, 193, 228, 261,
  494`), which forces `Content-Type: application/json` and thus a CORS preflight that is never
  answered. A `text/plain` or form-encoded "simple request" cannot bind the DTO.
- **I checked every route for a state-changing `GET`** across `Program.cs:120-533` (which `Lax`
  *would* permit on top-level navigation). There is none — all mutations are `POST`, `PATCH`, or
  `DELETE`. This is the check that would have made the finding High, and it passes.

### Where it does break

`SameSite` is **site**-scoped, not origin-scoped. `http://host:9000` and `http://host:8080` are the
same site; so is `http://evil.host.local` relative to `http://host.local`. So:

1. **AC8 precondition:** the attacker controls any other port or subdomain of the dashboard's host —
   another container on the same host with a published port, another app on the NAS, or a
   subdomain on the same reverse proxy.
2. From that origin, requests to the dashboard are same-site, so `Lax` attaches the cookie.
3. A `fetch` with `Content-Type: application/json` still needs CORS for the *response*, but the
   **request is sent and the side effect happens** — CORS does not prevent the state change, only
   the reading of the reply. So the attacker can fire
   `POST /api/settings/download-folder`, `PATCH /api/chats/{id}` (setting `folderTemplate` → F-02),
   or `POST /api/login/logout` (destroying `session.dat`) blind.
4. Because `Secure` is off (F-06), a plaintext same-site origin also works.

**Impact:** blind triggering of any state change, including the F-02 write primitive. Because the
precondition is a co-located attacker rather than "any web page", this is Medium — but the *absence*
of the control must still be fixed, since a single future state-changing `GET` or a relaxation of
`SameSite` would immediately make it High.

### Remediation

**File:** `TelegramAutoDownload.Headless/Program.cs`, `WebAuthMiddleware.cs`,
`WebAuthService.cs`, `wwwroot/app.js`.

1. **Add an `Origin`/`Sec-Fetch-Site` check in `WebAuthMiddleware`** for every unsafe method
   (`POST`, `PUT`, `PATCH`, `DELETE`): require `Sec-Fetch-Site: same-origin` when present,
   otherwise require `Origin` to exactly equal the configured external origin. Reject with `403`.
   This is the cheapest control and it closes the AC8 gap that `SameSite` cannot.
2. **Tighten the cookie to `SameSite=Strict`** at `WebAuthService.cs:140`. The dashboard is a
   single-page app with no cross-site entry flow, so `Strict` costs nothing functionally.
3. **Add a double-submit CSRF token** for defense in depth: issue a second non-`HttpOnly` cookie at
   `SignIn`, have `app.js` echo it in an `X-CSRF-Token` header (`app.js:132-136` is the single place
   to change), and require a constant-time match in the middleware.
4. **Codify the invariant:** add an architecture test asserting no `MapGet` handler mutates state,
   so the accidental mitigation cannot be lost silently.

### Regression tests to write

- `UnsafeMethod_WithForeignOrigin_Returns403` — `POST /api/settings/download-folder` with
  `Origin: http://evil.example`.
- `UnsafeMethod_WithNoOriginAndNoSecFetchSite_Returns403`.
- `UnsafeMethod_WithSameOrigin_Succeeds`.
- `Cookie_IsSameSiteStrict`.
- `NoMapGetEndpointMutatesState` — architecture test enumerating `MapGet` registrations.
- `CsrfToken_MissingOrMismatched_Returns403`.

---

## F-15 — Unbounded disk consumption: no maximum file size, no free-space check, no quota

- **Severity:** Medium
- **Exploitability:** Proven attack path — **no application credential required**
- **Reachable by default:** **Yes**
- **CWE:** CWE-400, CWE-770 (Allocation Without Limits)

### Evidence (verified)

- `BaseMessage.cs:274-291` (`CheckDownloadPolicy`) — the **only** size gate is a *minimum*:
  `if (chatDto.DownloadFromSize != 0 && documentSizeInMb < chatDto.DownloadFromSize)`. There is no
  maximum anywhere in this method.
- `DownloadOrchestrator.cs:94-161` — the dispatcher checks flood-wait state, chat existence, and
  prior-download state. It never consults file size or free space.
- `DownloadOrchestrator.cs:109-111` — the destination root is read from config and used directly.
- `BaseMessage.cs:112-155` — `DownloadDocumentAsync` streams to a `.part` file with retries and a
  3-minute inactivity watchdog, but **no byte ceiling**.
- No `DriveInfo`, `AvailableFreeSpace`, or quota check exists in the headless project. The WPF app
  has `Services/DiskSpaceService.cs` for *display only*, and it is not referenced by the headless
  path.
- `ConfigParams.cs:18` — `DownloadThreads` defaults to 3, so three concurrent unbounded transfers.
- `docker-compose.yml:26` — `/downloads` is a host bind mount, so exhaustion hits the host.
- `docker-compose.yml` has no `mem_limit`, `cpus`, or `storage-opt`.

### Attack path

**Preconditions:** a monitored chat that AC3 can post to. For any monitored public channel or group
this is the general public — **no dashboard credential, no plugin opt-in, no misconfiguration.**

1. AC3 posts large media (Telegram permits up to 2 GB per file for standard accounts, 4 GB for
   Premium) into a monitored chat, repeatedly.
2. `MediaTracker`/the orchestrator queues each item; the pipeline downloads all of them with no size
   gate and no free-space precondition.
3. The `/downloads` bind mount fills. Because `/data` is also a bind mount on the same host
   (`docker-compose.yml:25`), the SQLite queue database (`tad.db`), the WAL file (`Database.cs:40`),
   the log directory, and `session.dat` all lose the ability to write.
4. Secondary effect: a flood of media also drives Telegram API usage up, risking `FLOOD_WAIT` and
   account restriction — a risk the codebase itself takes seriously
   (`RateLimitThresholds`, `FloodWaitTracker`) and which an attacker can deliberately provoke.

**Impact:** availability loss for the service and the host, and potential data corruption of the
queue database when the volume fills mid-write.

### Remediation

**File:** `TelegramClient/Factory/Base/BaseMessage.cs`,
`TelegramAutoDownload.Headless/Scanning/DownloadOrchestrator.cs`, `TelegramClient/Models/ConfigParams.cs`,
`docker-compose.yml`.

1. Add `MaxFileSizeMb` (default 2048) and `MinFreeSpaceMb` (default 5120) to `ConfigParams`, with an
   API to set them and clamping as done for the existing limits (`Program.cs:228-242` is the pattern).
2. Enforce the maximum in `CheckDownloadPolicy` (`BaseMessage.cs:280`) right beside the existing
   minimum check, returning a `skipped` result with a clear reason so it surfaces in the dashboard's
   skipped-items table.
3. Enforce the free-space floor in `DownloadOrchestrator.DispatchAsync` before
   `ExecuteByMessageIdAsync` (`DownloadOrchestrator.cs:132`): if
   `new DriveInfo(downloadRoot).AvailableFreeSpace < row.size_bytes + MinFreeSpaceMb`, set the row
   to `Skipped` with a "insufficient disk space" reason and log a warning. Re-check inside the
   download loop for long transfers.
4. Add a per-chat and global byte quota (e.g. `MaxTotalBytesPerChat`), tracked from the existing
   `Media.size_bytes` aggregates that `MediaRepository.GetPerChatStatusCountsAsync` already computes.
5. Add `mem_limit`, `cpus`, and `pids_limit` to `docker-compose.yml`, and document a filesystem quota
   for the `/downloads` volume.

### Regression tests to write

- `CheckDownloadPolicy_FileOverMaxSize_IsSkipped` — assert `IsSuccess == false` and the reason names
  the limit.
- `CheckDownloadPolicy_MinAndMaxTogether_BehaveCorrectly` — min 10 MB, max 100 MB; assert 5/50/500 MB
  are skipped/accepted/skipped.
- `Dispatch_InsufficientFreeSpace_SkipsWithoutDownloading` — inject a free-space provider; assert no
  download is attempted.
- `Dispatch_ChatQuotaExceeded_Skips`.

---

## F-16 — Container hardening gaps: no `USER` in the image, unpinned bases and packages, no runtime restrictions

- **Severity:** Medium
- **Exploitability:** Theoretical / defense-in-depth individually; each one raises the impact of every other finding
- **Reachable by default:** **Yes**
- **CWE:** CWE-250 (Execution with Unnecessary Privileges), CWE-1104 (Use of Unmaintained/Unpinned Components)

### Evidence (verified)

- `Dockerfile:4` — `FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build`; `Dockerfile:32` —
  `FROM mcr.microsoft.com/dotnet/aspnet:8.0`. **Floating tags, no digest pin.** The image content
  changes silently between builds; a compromised or regressed upstream is adopted automatically.
- `Dockerfile:34-37` — `apt-get install -y --no-install-recommends yt-dlp ffmpeg ca-certificates`
  with **no version pins**, on top of an `apt-get update` in the same layer. Not reproducible.
- **`Dockerfile` contains no `USER` instruction** (verified across all 53 lines). The image therefore
  runs as **root** by default. The uid drop to `1029:100` exists only at
  `docker-compose.yml:11`, so `docker run telegram-auto-download:latest` — or any orchestrator that
  does not replicate that compose setting (Kubernetes, Swarm, Portainer templates) — runs the whole
  application as root.
- `docker-compose.yml` has **no** `read_only: true`, `cap_drop: [ALL]`,
  `security_opt: [no-new-privileges:true]`, `mem_limit`, `cpus`, `pids_limit`, or `tmpfs`.
- `Dockerfile:50-51` — `VOLUME ["/data","/downloads"]` and `EXPOSE 8080`, with no `HEALTHCHECK`.
- `Dockerfile:44-48` — `LISTEN_URL`/`ASPNETCORE_URLS` bind `0.0.0.0` (see F-06).
- `Dockerfile:28-29` — `dotnet publish ... --no-self-contained`, so the runtime image needs the
  full ASP.NET base rather than a minimal/chiseled one.

### Attack path

No direct path; this finding is an impact multiplier, which is why it is Medium rather than High.

1. Any code-execution or file-write primitive (F-02, F-12, F-13) executes with whatever the container
   grants. Under `docker run` without `--user`, that is **root inside the container**, with a
   writable `/app`, full capability set, and `no-new-privileges` unset — a far better position from
   which to attempt a container escape or to tamper with the application's own binaries and the
   `Plugins/` directory that `MessageTextFactoryService.cs:64` loads with `Assembly.LoadFrom`.
2. Floating base tags mean a future upstream compromise or regression is pulled in with no review
   and no way to reproduce the previously-good build.
3. No resource limits means F-15's disk exhaustion and F-19's connection flooding have no ceiling.

### Remediation

**File:** `Dockerfile`, `docker-compose.yml`.

1. Pin both base images by digest: `FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:<digest>`.
   Adopt a scheduled dependency-update job rather than floating tags.
2. Pin apt package versions at `Dockerfile:36` (`yt-dlp=<ver> ffmpeg=<ver>`), or install yt-dlp from
   a pinned release with a verified checksum (see F-10).
3. Add to the runtime stage, after `COPY --from=build`:
   ```
   RUN groupadd -g 100 app && useradd -u 1029 -g 100 -m app \
    && mkdir -p /data /downloads && chown -R 1029:100 /data /downloads
   USER 1029:100
   ```
   so the image is non-root **regardless of how it is run**. Keep the compose `user:` line as
   belt-and-braces.
4. Add to `docker-compose.yml`: `read_only: true` with `tmpfs: [/tmp]`, `cap_drop: [ALL]`,
   `security_opt: ["no-new-privileges:true"]`, `mem_limit`, `cpus`, `pids_limit`.
5. Add a `HEALTHCHECK` hitting a new unauthenticated `/healthz` that reports liveness only (no
   configuration state — see F-01 on pre-auth disclosure).
6. Consider the chiselled runtime (`aspnet:8.0-jammy-chiseled`) to shrink the attack surface;
   verify yt-dlp/ffmpeg packaging separately if adopted.
7. Add a container scan (Trivy/Grype) to CI and fail the build on High/Critical OS CVEs.

### Regression tests to write

- CI check: `Dockerfile` contains a `USER` instruction and every `FROM` includes an `@sha256:` digest
  (a simple lint script, run in CI).
- CI check: every `apt-get install` package is version-pinned.
- Integration test: `docker run` the image **without** `--user`, then assert `id -u` inside the
  container is not `0`.
- CI: Trivy scan gate on the built image.

---

## F-17 — Static files are served before the authentication middleware

- **Severity:** Low
- **Exploitability:** Theoretical / defense-in-depth *today*
- **Reachable by default:** **Yes**
- **CWE:** CWE-425 (Forced Browsing), CWE-696 (Incorrect Behavior Order)

### Evidence (verified)

`Program.cs:47-49`:
```
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebAuth();
```
`UseStaticFiles` short-circuits any matching request **before** `WebAuthMiddleware` ever runs. The
allowlist in `WebAuthService.cs:175-177` (`/`, `/index.html`, `/styles.css`, `/app.js`) is therefore
dead code for those paths — they were already served.

### Assessment

Today `wwwroot` contains exactly three files — `index.html`, `styles.css`, `app.js`
(confirmed via `git ls-files`) — all of which the allowlist intends to be public anyway. **Current
impact is effectively nil, so I am explicitly not inflating this.**

The defect is structural: the ordering means *any* file later added to `wwwroot` — a config dump, a
`.map` file, a diagnostic page, a backup — becomes anonymously readable without anyone editing
`IsPublicPath`, and no test would catch it. It also makes the allowlist misleading to the next
reader, who will reasonably assume it is the thing enforcing public access.

### Remediation

**File:** `TelegramAutoDownload.Headless/Program.cs`.

1. Move `app.UseWebAuth()` above `app.UseDefaultFiles()` / `app.UseStaticFiles()`, and let
   `IsPublicPath` be the single authority for what is anonymous.
2. Keep the allowlist minimal and explicit (`WebAuthService.cs:175-181`). Prefer a small
   `StaticFileOptions` with an explicit file provider scoped to the login assets over serving the
   whole directory anonymously.
3. Add `ServeUnknownFileTypes = false` and ensure directory browsing is never enabled.

### Regression tests to write

- `StaticFile_NotOnAllowlist_Returns401WhenUnauthenticated` — drop a fixture
  `wwwroot/secret.txt`; assert `401`, not `200`.
- `LoginAssets_AreServedAnonymously` — `/`, `/index.html`, `/app.js`, `/styles.css` → `200`.
- `Middleware_OrderPutsAuthBeforeStaticFiles` — architecture test on the pipeline registration order.

---

## F-18 — No security response headers and no CORS policy

- **Severity:** Low
- **Exploitability:** Theoretical / defense-in-depth
- **Reachable by default:** **Yes**
- **CWE:** CWE-1021 (Improper Restriction of Rendered UI Layers), CWE-693 (Protection Mechanism Failure)

### Evidence (verified)

Reading `Program.cs:25-49` — the entire pipeline configuration — there is no middleware emitting any
of: `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`,
`Permissions-Policy`, `Cross-Origin-Opener-Policy`. The only header set anywhere is
`CacheControl` on the `401` response (`WebAuthMiddleware.cs:22`) and on the SSE stream
(`Program.cs:336`). No `AddCors`/`UseCors` either.

### Assessment

Explicitly **not** a live vulnerability: the dashboard's own escaping is sound
(`app.js:603` escapes `& < > "`, and it is applied to every Telegram-derived string I traced — see
F-24 for the two cosmetic exceptions), and the absence of a CORS policy is the safe default.

What the missing headers cost:
- **No CSP** means any future DOM-injection regression — in a codebase that builds table rows with
  `innerHTML` in nine places (`app.js:499, 574, 776, 804, 859, 887, 992, 1062, 1103`) — goes straight
  from "escaping bug" to "full script execution with the operator's session". CSP is the control that
  makes F-24-class mistakes non-exploitable.
- **No `X-Frame-Options`/`frame-ancestors`** means the dashboard can be framed; combined with the
  absence of CSRF defenses (F-14) this enables clickjacking of the destructive actions
  (`Clear queue`, `Delete failed`, `Sign out of Telegram`).
- **No `X-Content-Type-Options: nosniff`** leaves MIME-sniffing open on the static and log responses.

### Remediation

**File:** `TelegramAutoDownload.Headless/Program.cs`.

Add a header middleware immediately after `app.UseWebAuth()`:
- `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'none'`
  — the dashboard uses no inline scripts or external resources (`index.html:7` links only
  `/styles.css`; inline SVG is markup, not script), so this policy needs no `unsafe-inline`.
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy: geolocation=(), camera=(), microphone=()`
- `Cross-Origin-Opener-Policy: same-origin`
Leave CORS unconfigured (no `AddCors`) so cross-origin reads stay blocked by default; if an API
consumer is ever needed, use an explicit origin allowlist with `AllowCredentials` never combined
with a wildcard.

### Regression tests to write

- `Response_IncludesSecurityHeaders` — assert each header on `/`, on a JSON API response, and on the
  `401` path.
- `Csp_DoesNotContainUnsafeInlineOrUnsafeEval`.
- `Cors_NoAccessControlAllowOriginByDefault`.

---

## F-19 — Log endpoints are an unbounded authenticated read channel, with a client-controlled offset that throws

- **Severity:** Low
- **Exploitability:** Proven attack path for the crash and the resource abuse (authenticated)
- **Reachable by default:** **Yes** (post-authentication)
- **CWE:** CWE-532 (Insertion of Sensitive Information into Log File), CWE-400, CWE-248 (Uncaught Exception)

### Evidence (verified)

- `Program.cs:291-357` — four endpoints: `/api/logs/files`, `/tail`, `/search`, `/stream`. All
  require a session (none is on the allowlist at `WebAuthService.cs:173-183`) — that part is correct.
- `Program.cs:319-357` — the SSE stream loop. `fromOffset` is a **client-supplied `long?`**
  (`:319`), used at `:334` and passed by `ref` into `ReadSinceOffset` (`:343`).
- `HeadlessLogReader.cs:137-148` — with a **negative** offset, `fs.Length < offset` is false and
  `fs.Length <= offset` is false, so execution reaches `fs.Seek(offset, SeekOrigin.Begin)` at `:144`,
  which throws for a negative position. The `try` at `Program.cs:339` catches **only**
  `OperationCanceledException` (`:356`), so the exception escapes after `ContentType` has been set at
  `:337` — an unhandled exception on a partially-started response.
  (The subsequent `new byte[len]` at `:146` would be the larger problem, but `Seek` throws first, so
  I am **not** claiming a memory-exhaustion primitive here.)
- `Program.cs:341-354` — the loop runs `while (!ct.IsCancellationRequested)` with
  `Task.Delay(1000)`, re-opening and reading the file each second. **There is no cap on concurrent
  SSE connections.**
- `HeadlessLogReader.cs:102-127` — `/api/logs/search` streams up to `MaxSearchScanLines = 2_000_000`
  lines (`:13`) per request, with no per-client throttle.
- `HeadlessLogReader.cs:54-84` — `/api/logs/tail` reads up to `MaxTailBytes = 2 MiB` (`:12`) per
  request.
- Log content: chat names, file names, download paths, and error detail
  (`DownloadOrchestrator.cs:123-126, 167-168, 178`, `BaseMessage.cs:300`). **I searched for
  credential leakage and found none** — see the note below.

### Assessment

**The "SSE as an exfiltration channel" framing is correct but narrower than stated.** The channel is
authenticated, so it is not a pre-auth leak. What it provides is a *live* feed of the operator's
Telegram activity — which chats are monitored, what file names are being downloaded, and to what
paths — to anyone holding a session cookie. Combined with F-04 (cookies cannot be revoked), a single
stolen cookie yields up to seven days of continuous surveillance with no way for the operator to cut
it off and no audit record that it happened.

**Correcting the log-hygiene lead:** the brief asked me to confirm whether phone numbers, 2FA
passwords, API hashes, or bot tokens reach the logs. **They do not, as far as the code shows.** The
only credential-adjacent log statements are `WebAuthService.cs:66` and `:127`, which log a username
only. `LoginCoordinator.cs:93` logs the exception object from a failed login, not the submitted
phone/code/password. `Program.cs:129-148` does not log request bodies. **[INFERRED]** residual risk:
WTelegramClient's own internal logger is never wired to Serilog (no reference to
`WTelegram.Helpers.Log` exists in the repository), so its default sink is unmanaged and unreviewed —
worth a follow-up, but not something I can evidence as a finding.

### Remediation

**File:** `TelegramAutoDownload.Headless/Program.cs`,
`TelegramAutoDownload.Headless/Logging/HeadlessLogReader.cs`.

1. **Validate `fromOffset`.** At `Program.cs:334`, clamp to `Math.Max(0, fromOffset ?? fileLength)`
   and additionally reject values greater than the current file length. Guard `ReadSinceOffset`
   itself at `HeadlessLogReader.cs:139` with `if (offset < 0) offset = 0;`.
2. **Broaden the SSE catch.** At `Program.cs:356`, also catch `IOException` and
   `ArgumentOutOfRangeException`, emit an SSE `event: error` frame, and close cleanly.
3. **Cap concurrency.** Add a semaphore (e.g. 4 concurrent SSE streams process-wide, 1 per session)
   and return `429` when exhausted. Apply the same rate limiter introduced in F-05 to
   `/api/logs/search` and `/api/logs/tail`.
4. **Redact at the sink, not at the reader.** Add a Serilog enricher/destructuring policy that masks
   anything resembling a phone number, a bot token (`\d{8,10}:[A-Za-z0-9_-]{35}`), or a 32-hex
   `api_hash`, so that a future careless `Log.Information` cannot leak through this channel.
5. **Keep the audit log out of this surface.** The separate audit sink from F-05 must live outside
   `LogsDir`, or `ListFiles`' `headless*.log` glob (`HeadlessLogReader.cs:23`) must be tightened so
   the audit trail is not readable — and therefore not tamper-assessable — through the same
   endpoints it is meant to hold accountable.

### Regression tests to write

- `LogStream_NegativeFromOffset_Returns400_NotUnhandledException`.
- `LogStream_FromOffsetBeyondEof_IsClamped`.
- `LogStream_ExceedsConcurrencyCap_Returns429`.
- `LogSearch_IsRateLimited`.
- `Logs_NeverContainBotTokenOrApiHash` — log a fixture config through the enricher; assert the
  values are masked.
- `ListFiles_DoesNotIncludeAuditLog`.

---

## F-20 — `WEB_PASSWORD` is silently ignored once `web-auth.json` exists

- **Severity:** Low
- **Exploitability:** Proven attack path (operator deception, not a direct exploit)
- **Reachable by default:** **Yes**
- **CWE:** CWE-1188, CWE-440 (Expected Behavior Violation)

### Evidence (verified)

`WebAuthService.cs:185-218` (`LoadCredentials`) checks `File.Exists(CredentialsFile)` **first** and
returns from the file if it parses (`:191-199`). Only if the file is absent or unparseable does it
fall through to `WEB_USERNAME`/`WEB_PASSWORD` (`:207-217`). There is no precedence documentation and
no log line stating which source won.

### Attack path

1. Operator deploys with `WEB_PASSWORD: changeme`, signs in, and changes the password via the
   dashboard. `TryChangePassword` writes `web-auth.json` (`WebAuthService.cs:124` → `:220-229`).
2. Later — after a suspected compromise, or during routine rotation — the operator edits
   `docker-compose.yml` to a new strong `WEB_PASSWORD` and restarts, reasonably believing the
   credential has rotated.
3. **Nothing changes.** `web-auth.json` still wins. The old password remains valid, and because of
   F-04 every pre-existing session is still valid too.
4. The operator now believes they have remediated an incident when they have not. That is the real
   harm: a false sense of containment.

The inverse also holds: `WEB_USERNAME` changes are ignored once the file exists, so the username in
compose can silently disagree with the username that actually works.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`.

1. Make the precedence explicit and safe: if `WEB_PASSWORD` is set, it **wins** over the file, and
   `LoadCredentials` re-hashes it and rewrites `web-auth.json`. Treat an env-supplied password as an
   operator-initiated rotation — which means it must also bump the credential generation / revoke
   sessions (F-04).
2. Log the source at startup: `Log.Information("Web credentials loaded from {Source}", ...)`.
3. Alternatively, if file-wins is the intended design, refuse to start when `WEB_PASSWORD` is set
   *and* differs from the stored hash, with a message telling the operator to delete
   `web-auth.json` to rotate. Silent divergence is the one outcome that must not remain.
4. Provide a documented, first-class rotation path: a `POST /api/web-auth/reset` for an
   operator-supplied bootstrap token, or a documented "delete `web-auth.json` and
   `.web-auth-secret`, then restart" runbook step.

### Regression tests to write

- `LoadCredentials_EnvPasswordSet_OverridesStoredFile` — write a file hash for `old`, set
  `WEB_PASSWORD=new`; assert `new` authenticates and `old` does not.
- `LoadCredentials_EnvRotation_InvalidatesExistingSessions`.
- `LoadCredentials_LogsCredentialSource`.

---

## F-21 — Single implicit operator: no user model, no roles, no audit trail, no MFA

- **Severity:** Low as an individual defect; **blocking** for enterprise deployment
- **Exploitability:** Theoretical / architectural (it is the absence of a control, not a reachable bug)
- **Reachable by default:** **Yes** — it is the architecture
- **CWE:** CWE-269 (Improper Privilege Management), CWE-778

### Evidence (verified)

- `WebAuthService.cs:27-30` — the entire identity model is three fields: `_username`,
  `_passwordHash`, `_usingDefaultPassword`. One user. No roles, no groups, no per-capability
  permissions.
- `WebAuthMiddleware.cs:15-19` — authorisation is binary: a valid session grants **every**
  non-public endpoint.
- `Program.cs:157-533` — no handler performs any authorisation check beyond the middleware, and no
  handler logs *who* performed the action. `TryGetSession` does return a username
  (`WebAuthService.cs:169`), but the middleware discards it (`WebAuthMiddleware.cs:15` —
  `TryGetSession(ctx.Request, out _)`), so the identity is not even available to handlers.
- No MFA, no SSO/OIDC/SAML, no API tokens with scopes, no password-expiry or history policy.
- Destructive operations have no elevated gate: `POST /api/login/logout` (`Program.cs:150-154`)
  deletes `session.dat` (`TelegramApp.cs:149`), forcing full SMS re-verification;
  `POST /api/queue/delete-failed` and `/api/queue/clear` (`Program.cs:475-486`) delete database
  rows — all reachable with one plain session and none recorded against an actor.

### Assessment

This is the finding that determines whether the product can be sold into an enterprise at all,
independent of any specific bug. Concretely it means: an incident cannot be investigated (no actor,
no timestamp, no source IP on any action), credentials cannot be shared between two operators
without becoming a shared secret, no action can be attributed or disputed, and the blast radius of
any credential compromise is by construction 100% — including asset A1.

I am rating it Low on the exploitability axis, because there is no attack path to demonstrate, while
flagging it as a **release blocker** on the business axis. Both statements are true and the
distinction matters for sequencing: it is a larger project than F-01..F-06 and should not displace
them.

### Remediation

**File:** new `Data/Schema/00N_users.sql`, `WebAuthService.cs`, `WebAuthMiddleware.cs`, `Program.cs`.

Phase this; do not attempt it in one change.

1. **Phase 1 — attribution (cheap, do it with F-05).** Stop discarding the username at
   `WebAuthMiddleware.cs:15`: store it in `HttpContext.Items`/`ClaimsPrincipal`. Emit a structured
   audit event for every state-changing request with actor, action, target, source IP, and outcome,
   to the dedicated audit sink from F-05.
2. **Phase 2 — user store.** Add a `User` table (`id`, `username`, `password_hash`, `algorithm`,
   `role`, `created_at`, `disabled_at`, `credential_generation`) with the Argon2id parameters from
   F-03. Migrate the single `web-auth.json` operator on first run.
3. **Phase 3 — roles.** Introduce at minimum `Viewer` (read-only: status, queue, logs) and
   `Operator` (mutations), then `Admin` (user management, credentials, download-folder). Enforce via
   ASP.NET Core authorisation policies per endpoint rather than ad-hoc checks. Put
   `POST /api/settings/download-folder`, `POST /api/settings/credentials`, and
   `POST /api/login/logout` behind `Admin` plus a re-authentication step.
4. **Phase 4 — federation and MFA.** Add TOTP for local accounts and OIDC for enterprise SSO, so
   customers can bring their own identity provider and lifecycle.

### Regression tests to write

- `StateChangingRequest_EmitsAuditEventWithActorAndIp` — parameterised across every mutating route,
  so a newly added endpoint without auditing fails the suite.
- `ViewerRole_CannotMutate` — assert `403` on every mutating route.
- `AdminOnlyRoutes_RejectOperatorRole`.
- `DisabledUser_CannotAuthenticate_AndExistingSessionsAreRevoked`.

---

## F-22 — WPF app launches downloaded files with `UseShellExecute = true`

- **Severity:** Low
- **Exploitability:** Proven attack path, but user-assisted
- **Reachable by default:** **Yes for the WPF product**
- **CWE:** CWE-494, CWE-73

### Evidence (verified)

- `MainWindow.xaml.cs:770` and `:775` —
  `Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true })`. `FilePath`
  originates from the download pipeline (`BaseMessage.cs:153` sets `FilePath = finalPath`), i.e. the
  file name came from Telegram.
- `BaseMessage.cs:252-262` sanitises the file name against `Path.GetInvalidFileNameChars()` — which
  blocks traversal (see the threat model §7 correction) but **deliberately preserves the extension**.
  `evil.exe`, `evil.lnk`, `evil.hta`, `evil.js`, and `evil.scr` all survive sanitisation intact.
- `MainWindow.xaml.cs:727` — `Process.Start("explorer.exe", path)` passes a config-derived path as a
  single argument string rather than via `ArgumentList`; a path containing quotes can influence the
  command line.

### Attack path

1. AC3 posts `Invoice_2026.pdf.exe` (or a `.lnk`) to a monitored chat.
2. It downloads normally and appears in the Downloads panel with an **Open** (📂) button
   (`README.md:18`).
3. The operator clicks Open — a completely ordinary action that the UI is designed to encourage.
   `UseShellExecute = true` hands the file to the shell, which **executes** it rather than opening
   it in a viewer.

**Impact:** code execution as the desktop user. Rated Low only because it requires the operator to
click; the Windows shell's extension-hiding behaviour makes that click quite likely.

### Remediation

**File:** `TelegramAutoDownload/MainWindow.xaml.cs`.

1. Never shell-execute a downloaded file. At `:770` and `:775`, replace with "reveal in Explorer":
   `Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", item.FilePath } })`.
   The operator can then decide what to do, with the real extension visible.
2. If opening must be supported, maintain an allowlist of safe extensions
   (`.jpg .png .gif .webp .mp4 .mkv .mp3 .m4a .pdf .txt`), and for anything else show a warning
   dialog that states the full extension and requires explicit confirmation.
3. Use `ArgumentList` instead of a concatenated argument string at `:727` to remove the quoting
   issue.
4. Consider applying the Zone.Identifier mark-of-the-web to downloaded files so SmartScreen and
   Office protected view engage.

### Regression tests to write

- `OpenFile_ExecutableExtension_DoesNotShellExecute` — inject a process launcher; assert the
  invocation is `explorer.exe /select,` and never the file itself.
- `OpenFile_AllowlistedMediaExtension_Opens`.
- `OpenFolder_PathWithQuotes_IsPassedViaArgumentList`.

---

## F-23 — Username validity is observable through a timing side channel

- **Severity:** Low
- **Exploitability:** Theoretical / defense-in-depth
- **Reachable by default:** Yes
- **CWE:** CWE-208 (Observable Timing Discrepancy)

### Evidence (verified)

`WebAuthService.cs:79-85`:
```
if (!string.Equals(username?.Trim(), _username, StringComparison.Ordinal))
    return false;
return CryptographicOperations.FixedTimeEquals(HashPassword(password ?? ""), _passwordHash);
```
A wrong username returns **before** any hashing. A correct username performs the hash. The password
comparison itself is correctly constant-time — the leak is the early return, not the compare.

### Assessment

With the current SHA-256 hash (F-03) the timing difference is microseconds and lost in network
noise, so I am **not** claiming a practical attack. It becomes genuinely measurable the moment F-03
is fixed: an Argon2id verification takes tens of milliseconds, turning this into a clear, remotely
observable oracle for "is this the right username" — and the username is the one credential half
that operators customise via `WEB_USERNAME` and therefore treat as semi-secret.

Fix it **in the same change as F-03**, or the password-storage fix will introduce a new oracle.

### Remediation

**File:** `TelegramAutoDownload.Headless/WebAuthService.cs`.

Always perform the KDF, then combine both comparisons without short-circuiting:
```
var userOk = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(username?.Trim() ?? ""), Encoding.UTF8.GetBytes(_username));
var passOk = VerifyPassword(password ?? "", _passwordHash);   // always runs
return userOk & passOk;                                        // non-short-circuiting &
```
Note the non-short-circuiting `&`. For the unknown-user case, verify against a fixed dummy hash with
identical parameters so the work performed is indistinguishable. Keep the response body and status
identical for every failure mode (`Program.cs:76-78` already does this correctly).

### Regression tests to write

- `ValidateCredentials_WrongUsername_StillPerformsKdf` — assert elapsed time for a wrong username is
  within 25% of that for a wrong password (loose enough not to be flaky, tight enough to catch a
  reintroduced early return).
- `Login_WrongUsernameAndWrongPassword_ReturnIdenticalResponses` — compare status, headers, and body.

---

## F-24 — Two unescaped interpolations in the dashboard (informational)

- **Severity:** Informational
- **Exploitability:** Theoretical — no attacker-controlled source reaches either site today
- **Reachable by default:** n/a
- **CWE:** CWE-79

### Evidence (verified)

The dashboard's escaping is, on the whole, correct. `app.js:603` defines
`escape()` covering `& < > "`, and I traced it to every Telegram-derived value rendered via
`innerHTML`: chat name and username (`:501`), type (`:502`), filter and folder template (`:517-518`),
sync tooltip (`:520`), bootstrap job name and error (`:777-778`), queue chat name (`:805`), skipped
and failed file names, kinds, and error text (`:862-865`, `:890-894`), download chat/file/plugin/error
(`:993-999`), and the test-download summary (`:574`). Log lines use `escapeHtml` (`:1056-1058`) into
a text position. **No stored-XSS path from Telegram content was found.**

Two residual spots:

1. `app.js:996` — `<span class="pill ${d.status.toLowerCase()}">${d.status}</span>`. `d.status` is
   unescaped in both the attribute and the text. Its source is the server-side enum
   `DownloadStatus.ToString()` (`Program.cs:511`, enum at `HeadlessHost.cs:10`), so the value set is
   fixed and safe **today**.
2. `app.js:1103` — `<option value="${escapeHtml(f.name)}">`. `escapeHtml` (`:1056-1058`) escapes only
   `& < >`, **not `"`**, and this is a double-quoted attribute. `f.name` comes from a directory
   listing constrained to `headless*.log` (`HeadlessLogReader.cs:23`), so a quote cannot currently
   appear.

Both are latent: safe by virtue of their current data source, not by construction.

### Remediation

**File:** `TelegramAutoDownload.Headless/wwwroot/app.js`.

1. Delete `escapeHtml` (`:1056-1058`) and use the single `escape()` (`:603`) everywhere, extended to
   also escape `'` and `` ` `` so it is safe in any quoting style.
2. Apply it at `:996` to both the class attribute and the text.
3. Prefer `textContent` and `document.createElement` over `innerHTML` for the nine table-row
   builders; at minimum, route every interpolation through the one escaper.
4. Add the CSP from F-18 — that is what makes this class of mistake non-exploitable rather than
   merely absent.

### Regression tests to write

- `escape_HandlesAllFiveHtmlMetacharacters` — `& < > " '`.
- `renderDownloads_StatusIsEscaped` — inject a crafted status; assert no raw `<` reaches the DOM.
- `loadLogFiles_FileNameWithQuote_DoesNotBreakOptionAttribute`.
- A DOM-based test asserting `#log-view` and the chat table contain no `<script>` when fed hostile
  chat names and file names.

---

## F-25 — Secret files are not gitignored (informational; corrects a lead)

- **Severity:** Informational
- **Exploitability:** Theoretical
- **Reachable by default:** n/a
- **CWE:** CWE-540 (Inclusion of Sensitive Information in Source Code)

### Evidence (verified)

`git check-ignore -v` resolves:
- `.env` → `.gitignore:8` — **ignored**
- `session.dat` → `.gitignore:30` (`*.dat`) — **ignored**
- `config.json`, `web-auth.json`, `.web-auth-secret` → **no match; not ignored**

`git ls-files` confirms **no secret is currently committed** (the only matches for
`session|secret|web-auth` are `TelegramClient/SessionLockHelper.cs` and its test).

**Correcting a lead:** the brief's suspicion that the README's claim at `README.md:107` ("`.env` is
in `.gitignore` — it is never committed") might be false is **unfounded — the claim is true.** The
residual gap is only the three unignored files. In normal operation they live in
`HeadlessPaths.DataDir` (`/data`) outside the working tree, so the practical risk is limited to a
developer who sets `DATA_DIR` to the repository root — plausible during local debugging, since
`HeadlessPaths.cs:12-15` falls back to `SpecialFolder.ApplicationData` and a developer working
around that may well point it at `.`.

### Remediation

**File:** `.gitignore`.

Append:
```
# Runtime secrets and state — never commit
web-auth.json
.web-auth-secret
config.json
tad.db
tad.db-wal
tad.db-shm
/data/
/downloads/
```
Add a pre-commit secret scanner (gitleaks or `detect-secrets`) to CI, and add a `.gitattributes`
entry or a CODEOWNERS rule so changes to `.gitignore` in this area get reviewed.

### Regression tests to write

- CI: gitleaks scan of the full history, failing the build on any hit.
- CI: assert `git check-ignore` succeeds for each of `web-auth.json`, `.web-auth-secret`,
  `config.json`, `tad.db`.

---

## F-26 — Unverified: yt-dlp argument construction inside `ManuHub.Ytdlp.NET`

- **Severity:** Low **pending verification** — could be High if the library builds a shell string
- **Exploitability:** **Unverified.** I could not read the dependency in this environment
- **Reachable by default:** Unknown (depends on the plugin being enabled)
- **CWE:** CWE-88 (Argument Injection) — *candidate only*

### What is verified

- `MessageTextFactoryService.cs:123` — `Text = line`, a raw line of the Telegram message.
- `SocialMediaPlugin.cs:168` and `YouTubePlugin.cs:122` —
  `await ytdlp.DownloadAsync(config.Text, cancelToken)`. The attacker-controlled string is handed
  to the library as the URL.
- `SocialMediaPlugin.cs:117-128` / `YouTubePlugin.cs:78-88` — other arguments (format, output
  template, cookies path) come from config, and the output template embeds `config.ChatName` via
  `PluginFolderPathHelper.CombineUnderDownloadRoot`, which is also externally influenced (a chat's
  display name is set by whoever controls the channel).

### What is NOT verified

I could not read `ManuHub.Ytdlp.NET`'s implementation — the package was not present in the local
NuGet cache and I did not build (the brief forbids `dotnet build`). So I **cannot** say whether it:

- uses `ProcessStartInfo.ArgumentList` (safe — each argument passed as a separate array element), or
- concatenates into `ProcessStartInfo.Arguments` (a single string, where a URL containing `"` could
  terminate one argument and begin another), or
- invokes through `/bin/sh -c` or `cmd.exe /c` (worst case — shell metacharacters live).

**If the second or third is true, this is a High-severity remote code execution reachable by AC3**
with no application credential: yt-dlp exposes `--exec CMD`, which runs an arbitrary command after
download, so a single injected argument is sufficient. I am recording it at Low **explicitly because
I could not reach it**, per the no-CVSS-theater rule — not because the risk is small.

### Required verification step (do this first)

Decompile or read the source of the `Ytdlp` class and inspect how `DownloadAsync` constructs its
process invocation. Specifically look for `ProcessStartInfo.Arguments` assignment, string
concatenation of the URL, or any `UseShellExecute = true`. Then re-rate this finding.

### Remediation (regardless of the verification outcome)

**File:** `SocialMediaPlugin/SocialMediaPlugin.cs`, `YoutubePlugin/YouTubePlugin.cs`, possibly
replacing the dependency.

1. **Do not rely on the library's quoting.** Validate before the call: require an absolute `https`
   URI via `Uri.TryCreate`, reject any string containing `"`, `'`, `` ` ``, `\`, `;`, `&`, `|`,
   `$`, newline, or carriage return, and reject anything starting with `-` (which yt-dlp would
   interpret as a flag). Add this to the shared host-allowlist helper from F-12.
2. Prefer `--` as an argument separator before the URL so yt-dlp cannot interpret it as an option,
   if the library allows passing raw arguments.
3. If the library concatenates or shells out, replace it with a direct
   `ProcessStartInfo { FileName = ytdlpPath, ArgumentList = { ... } }` invocation. `ArgumentList`
   never goes through a shell and is the correct primitive here. This removes an entire dependency
   from the trust boundary.
4. Sanitise `config.ChatName` before it reaches the output template
   (`SocialMediaPlugin.cs:79-89`, `YouTubePlugin.cs:43-53`) — it is externally controlled and flows
   into a yt-dlp `-o` template where `%` sequences are meaningful.
5. Run yt-dlp with reduced privileges and a restricted egress policy regardless of the outcome
   (see F-12).

### Regression tests to write

- `Download_UrlWithShellMetacharacters_IsRejectedBeforeProcessStart` — `https://youtu.be/a" --exec "calc`,
  and variants with `;`, `&&`, backtick, newline.
- `Download_UrlStartingWithDash_IsRejected`.
- `Download_ArgumentsArePassedViaArgumentList` — assert the constructed `ProcessStartInfo` has an
  empty `Arguments` string and a populated `ArgumentList`.
- `ChatNameWithPercentToken_DoesNotAlterOutputTemplate`.

---

## Appendix A — Ranked remediation order

Ranked by *real-world exploitability × business impact*, and sequenced so each step makes the next
one safe. Pair the bracketed items — they share a file and a test run, and splitting them creates a
window where the fix is worse than the bug.

| # | Finding | One-line justification |
|---|---|---|
| 1 | **F-01** Default credentials + pre-auth oracle | Zero-cost, zero-skill full compromise in the shipped configuration; the endpoint literally tells the attacker the password works. |
| 2 | **F-02** Arbitrary file write | Converts any dashboard access into permanent auth control and loss of the Telegram session; nothing else matters while this is open. |
| 3 | **F-03 + F-23** Argon2id, and fix the timing oracle together | The hash guarding asset A1 is trivially crackable, and adding a real KDF *creates* F-23's oracle unless both land in one change. |
| 4 | **F-05** Rate limiting, lockout, and security logging | Without it, the F-01/F-03 fixes are still brute-forceable and no compromise is detectable or investigable. |
| 5 | **F-04** Revocable sessions | Until logout and password change actually invalidate, no incident can be contained — every other fix is undone by one stolen cookie. |
| 6 | **F-06** Secure cookies, HSTS, forwarded headers | Stops the cookie leaking on the plaintext hop that F-04 makes irrevocable; also a prerequisite for F-05's IP partitioning. |
| 7 | **F-07** File modes + decouple the session key from `api_hash` | One file read currently yields both A1 and A2; this is the cheapest large reduction in blast radius. |
| 8 | **F-15** File-size and free-space limits | The only finding a total stranger can trigger with a single Telegram message, with no credential and no opt-in. |
| 9 | **F-26** Verify the yt-dlp argument path, then **F-12** fix the host allowlist | Unknown-severity RCE candidate reachable by the same credential-less actor; verify first, because the answer sets F-12's priority. |
| 10 | **F-09 + F-10** Signature verification for both update paths | Every desktop install silently executes remotely-sourced binaries on startup; a single upstream compromise reaches all users at once. |

Immediately after the top ten, and cheap enough to fold into the work above: **F-08** (refuse to
start with auth disabled outside Development), **F-14** (`Origin` check plus `SameSite=Strict`),
**F-17** (middleware order), **F-18** (security headers — the control that makes F-24 unexploitable),
**F-20** (env-password precedence), **F-25** (`.gitignore`).

Then, as scoped projects rather than patches: **F-13** (regex validation and timeouts),
**F-11** (torrent exposure and caps), **F-16** (container hardening), **F-22** (WPF shell-execute),
and **F-21** (users, roles, and audit — the enterprise release blocker, and the largest piece of
work in this list).

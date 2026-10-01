# ADR-008 — Separate configuration from secrets; encrypt key material at rest

- **Status:** Proposed
- **Date:** 2026-09-30
- **Deciders:** Architecture, Security
- **Related:** ADR-001, ADR-007

## Context

Configuration and secrets are the same JSON file today, and the key material is unprotected.

**Secrets in cleartext config.** `ApiHash` and `BotToken` are plain string properties on `ConfigParams` (`TelegramClient/Models/ConfigParams.cs:12,15`), serialised in full by `ConfigStore.Save` (`ConfigStore.cs:41`) to `/data/config.json`, and returned wholesale by `ReadConfig()` to every caller including HTTP endpoints (`HeadlessHost.cs:241`, used at `Program.cs:159,201,247,421,490,521`).

**A default password committed to version control.** `docker-compose.yml:17` ships `WEB_PASSWORD: "changeme"`, matching the `DefaultPassword` constant (`WebAuthService.cs:16`). Using it produces only a log warning (`WebAuthService.cs:69-71`) — the server still binds `0.0.0.0:8080` (`Program.cs:42`).

**Password hashing is unsalted SHA-256.** `HashPassword` is a bare `SHA256.HashData` over the UTF-8 password (`WebAuthService.cs:248-249`) — no salt, no stretching, offline-crackable at billions of guesses per second. The comparison itself is correctly constant-time (`:83`), which makes the weakness easy to overlook in review.

**The cookie signing key is plaintext and unrotatable.** Written base64 to `/data/.web-auth-secret` (`WebAuthService.cs:237-245`). The cookie payload is `{username}|{expires}|{sig}` (`WebAuthService.cs:134-136`) with **no key id**, so rotation invalidates every session instantly with no overlap window. There is no rotation endpoint.

**Cookies are insecure by default.** `Secure` is opt-in via `WEB_AUTH_SECURE_COOKIES` and defaults to `false` (`WebAuthService.cs:141`).

**`session.dat` is unencrypted.** It is a live Telegram auth key granting full account access — read every message, impersonate the user — sitting in plaintext on a mounted volume at a path hardcoded in `TelegramApp.cs:101-104`.

**No config schema or validation.** `ConfigStore.Read()` deserializes and patches exactly two fields (`ConfigStore.cs:30-34`); anything else is accepted. There is no `schemaVersion`, no validation, and no redaction in logging.

## Decision

**Three separate concerns, three separate mechanisms.**

### 1. Configuration — non-secret, may be logged

- `appsettings.json` for static settings, environment variables for deployment settings, `config.json` v2 for user-mutable settings.
- `config.json` gains `schemaVersion` (ADR-007), a published JSON Schema, and `IOptions<T>` + FluentValidation on load. Invalid config fails startup with a field-level message.
- **Secrets are stored as opaque references only** — `"apiHash": "secret://telegram/api-hash"`.
- `ConfigStore.Save` adopts atomic temp-write-and-rename, using the pattern already correct at `FileDownloadIndex.cs:115-118`, replacing the bare `File.WriteAllText` at `ConfigStore.cs:41`.

### 2. Secrets — `ISecretStore`

```csharp
public interface ISecretStore
{
    Task<string?> GetAsync(SecretRef r, CancellationToken ct);
    Task SetAsync(SecretRef r, string value, CancellationToken ct);
    Task<KeyRotationResult> RotateAsync(SecretRef r, CancellationToken ct);
}
```

Backends, selected by configuration: DPAPI (Windows desktop), libsecret or AES-GCM-with-file-key (Linux container), environment/Docker secrets, and Azure Key Vault / AWS Secrets Manager / HashiCorp Vault for enterprise deployments. `ApiHash` and `BotToken` are removed from `ConfigParams` and resolved through the store at use time.

### 3. Key material

| Artifact | Change |
|---|---|
| `session.dat` | AES-GCM at rest, DEK from `ISecretStore`. WTelegramClient accepts a session-store delegate, so this wraps cleanly. **Highest-value at-rest change in the system.** |
| Dashboard password | Argon2id (or PBKDF2-SHA256 ≥ 600k iterations if a new dependency is unacceptable), per-user salt, versioned hash prefix `$argon2id$v=19$...`, transparent rehash on next successful login |
| Cookie signing key | Keyring: sign with the newest `kid`, accept the previous for a 7-day overlap; add `kid` to the cookie payload (`WebAuthService.cs:134-136`) |

### 4. Secure by default

- `Secure` cookie defaults to **true** unless explicitly disabled (invert `WebAuthService.cs:141`).
- **Refuse to bind a non-loopback address while the password is still the default** (`WebAuthService.cs:16`), replacing the log-warning-only behaviour at `:69-71`.
- Delete `WEB_PASSWORD: "changeme"` from `docker-compose.yml:17`; document a generated-secret flow, and generate a random password on first start if none is supplied, printing it once to the log.
- A redacting Serilog formatter that cannot emit a `secret://` resolved value.

### 5. Rotation as a first-class operation

`POST /api/system/rotate/{web-signing-key|session-dek}`, each writing an audit record. No rotation path exists today for any key.

## Consequences

**Positive**

- No plaintext credential on disk. A stolen volume snapshot no longer yields a working Telegram session or an API hash.
- Encrypting `session.dat` closes the single highest-severity at-rest exposure: full account takeover from a backup tarball.
- Argon2id makes offline cracking economically infeasible, which matters because these deployments are frequently exposed to a LAN or a reverse proxy.
- Key rotation with an overlap window becomes routine rather than a forced mass logout.
- Refusing to expose a default-password instance closes the most common real-world misconfiguration — and the compose file currently *encourages* it.
- Key Vault / Secrets Manager backends are usually a hard procurement requirement for enterprise buyers.

**Negative**

- Losing the DEK means losing the session and requiring an interactive SMS re-auth. Mitigated by documenting DEK backup as a first-class operational step and by supporting an external KMS where the DEK is not local.
- Argon2id costs ~100 ms and ~64 MB per login attempt. Irrelevant for an admin dashboard, and it doubles as rate limiting.
- `ISecretStore` adds indirection to two hot-ish reads. Mitigated by caching resolved secrets in memory for the process lifetime.
- Secure-by-default cookies break plain-HTTP LAN access. Mitigated by an explicit opt-out env var and a clear error message naming it.
- Existing deployments must run a one-time secret migration. Automated, backed up, and idempotent (ADR-007).

**Neutral**

- Windows desktop keeps DPAPI, which is the correct platform primitive and requires no user-visible change.

## Alternatives considered

### A. Encrypt the whole `config.json` with one key

Simpler than reference-based secrets. Rejected: it makes the entire configuration opaque to operators, breaks GitOps-style config review, and still leaves the DEK problem while removing the ability to diff or hand-edit non-secret settings.

### B. Keep secrets in environment variables only

Already partially the case — `ConfigParams` reads `APP_ID` / `API_HASH` / `BOT_TOKEN` as initialiser fallbacks (`ConfigParams.cs:11,12,15`). Rejected as the whole answer: environment variables are visible in `/proc/<pid>/environ`, in `docker inspect`, and in crash dumps; they cannot be rotated without a restart; and they do not solve `session.dat` or the password hash. Retained as one `ISecretStore` backend.

### C. Bcrypt instead of Argon2id

Acceptable and widely deployed. Argon2id preferred for memory-hardness against GPU attack and because it is the current OWASP first recommendation. PBKDF2-SHA256 at ≥ 600k iterations is the fallback if adding a dependency is blocked, since it is in the BCL.

### D. Delegate authentication entirely to a reverse proxy (OAuth2 Proxy, Authelia)

Genuinely attractive for enterprise deployments and should be **supported** — `WEB_AUTH_ENABLED=false` already exists (`WebAuthService.cs:50-57`) and a trusted-header mode is a small addition. Rejected as the *only* option: the product also ships to users running a bare container on a NAS, exactly the `docker-compose.yml` scenario, where built-in auth must be safe by default.

### E. Do nothing about `session.dat`

Rejected. It is the highest-value secret in the system — full account access — and it is currently the *only* one with no protection at all, not even a hashed representation.

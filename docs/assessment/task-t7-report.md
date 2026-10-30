# Task T7 — Truthful docs, licensing, repo hygiene, CI consistency

**Track:** T7 (documentation & hygiene only; no C# changes by this track)  
**Intended branch:** `er/t7-docs-hygiene` from `enterprise-readiness` (`a458fd3`)  
**Controller note:** Work was done in shared checkout `D:\Codebase\TelegramDownloader`; no git commits from T7 agent per controller override.

## Files created

| Path |
|------|
| `LICENSE` |
| `NOTICE` |
| `SECURITY.md` |
| `docs/assessment/task-t7-report.md` (this file) |

## Files modified

| Path |
|------|
| `README.md` |
| `CHANGELOG.md` |
| `.gitignore` |
| `installer/setup.iss` |
| `docs/compliance/drafts/README.md` |
| `docs/ops/05-operations.md` |
| `.github/workflows/pr-validation.yml` |
| `.github/workflows/scheduled-maintenance.yml` |
| `.github/workflows/codeql.yml` |

**Not modified (ownership / scope):** `docker-publish.yml`, `Dockerfile`, any `.cs`/`.csproj`, `progress-ledger.md`.

## README claims corrected

| Claim | Change |
|-------|--------|
| Quiet Hours | Removed from features, settings, tests list, architecture (`QuietHoursHelper`) |
| Per-chat yt-dlp quality | Stated always best available; Quality column marked legacy |
| Dark Mode | Removed from Settings table |
| Settings export | API hash and bot token **excluded** (aligned with parallel export fix) |
| Active downloads pinned to top | Replaced with enqueue-time sort (newest first) |
| Plugin table | Priorities 2/3/10/100; SocialMedia allowlist; **Other** (UI “Direct”) for HTTP; native Telegram outside plugin chain |
| Folder `{Year}/{Month}/{Day}` | Documented as download/save date, not message date |
| Speed / ETA | Speed described as cumulative average |
| Build/tests | `RunnerApp.sln`; both test projects documented |
| New | Unofficial-client disclaimer; Security notes (`WEB_PASSWORD`, session path, account risk) |
| License | Links to `LICENSE` and `NOTICE` |

## Licensing & installer

- Root MIT `LICENSE` (Option C, Copyright (c) 2026 TelegramAutoDownload contributors).
- Root `NOTICE` with GPL-2+ Debian ffmpeg source pointers (`snapshot.debian.org`, `apt-get source`).
- `SECURITY.md` — GitHub private vulnerability reporting.
- `installer/setup.iss`: `LicenseFile=..\LICENSE`; installs `LICENSE.txt` and `NOTICE.txt`.

## Repo hygiene (controller actions)

`.gitignore` updated for `data/`, `downloads/`, `_baseline_cov/`, `TestResults/`, and explicit `data/*` secrets/DB files. `artifacts/` and `coverage*.xml` were already ignored.

**Untrack (run from repo root on T7 branch):**

```powershell
git rm --cached -r _baseline_cov
```

Verify: `git ls-files _baseline_cov` should be empty; folder may remain on disk.

## CI consistency

- **`pr-validation.yml`:** Builds `RunnerApp.sln`; Windows `TelegramAutoDownload.Tests` + ubuntu `TelegramAutoDownload.Headless.Tests`; shared `coverage-gate` over Cobertura artifacts; removed ephemeral sln synthesis.
- **`scheduled-maintenance.yml`:** GHCR image `ghcr.io/<owner>/<repo>` (lowercase), matching `docker-publish.yml`; yt-dlp canary uses same tag; `Docker Publish` workflow name in remediation text; `RunnerApp.sln` for NuGet sweep.
- **`codeql.yml`:** Builds `RunnerApp.sln`; removed UNVALIDATED banner.

### actionlint

**Ran:** yes (`actionlint` v1.7.7, Windows amd64) on `pr-validation.yml`, `scheduled-maintenance.yml`, `codeql.yml`.

**Output:** exit code 0, no diagnostics.

**Banners kept:** `release-desktop.yml` still marked UNVALIDATED (not edited / not linted in this pass).

## Open items

1. Controller: stage explicit paths, logical commits, exclude `data/` and `progress-ledger.md` cleanup.
2. First CI run may need coverage gate tuning if Windows Cobertura + headless XPlat paths differ.
3. `TelegramAutoDownload.Tests` has no `coverlet.collector`; Windows job uses `Code Coverage;Format=cobertura`.
4. README export wording assumes parallel T export fix lands (credentials excluded in code).
5. Other parallel tracks may have overlapping edits in the same checkout — reconcile before merge.

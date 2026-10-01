# 05 — CI/CD Pipelines: documentation, required secrets, validation checklist

> ## ⚠ EVERY WORKFLOW IN THIS CHANGE IS **UNVALIDATED**
>
> The five files under `.github/workflows/` were authored **without being executed
> even once**. The task constraints forbade running `dotnet build` or `dotnet test`
> in this workspace (another agent owns the build machine; concurrent builds cause
> file-lock conflicts), and GitHub Actions workflows cannot be run locally with any
> fidelity — Windows runners, Chocolatey-installed Inno Setup, GHCR pushes, and the
> `gh release` API surface are all unavailable here.
>
> What **was** verified:
>
> - All five files parse as valid YAML (`yaml.safe_load`, no errors).
> - Every `path:line` claim embedded in the comments was checked against the tree.
> - Version-extraction regexes were matched against the real `AppVersion.cs:5`
>   (`Current = "2.9.3"`) and `installer/setup.iss:2` (`#define AppVersion "2.9.3"`).
>
> What was **not** verified: that any step actually succeeds. Treat the first run of
> each workflow as a debugging session, not a release. §5 is the ordered checklist.

---

## 1. Why there was nothing here before

Before this change the repository had **no `.github/` directory at all**. Every
build, test, installer compile, and release upload was performed by hand from a
developer workstation following `.cursor/rules/release-process.mdc`. The practical
consequences:

- The mandatory both-artifacts invariant (`release-process.mdc:10-15`) was enforced
  only by an operator remembering to read the rule.
- No test ran automatically before a release.
- No dependency, secret, or container vulnerability scanning existed.
- The shipped artifact depended on whichever SDK the release machine had — which is
  currently **10.0.401** against `net8.0` targets (see
  [`05-operations.md`](./05-operations.md) §6).

## 2. The five workflows

| File | Trigger | Runners | Purpose |
|---|---|---|---|
| `pr-validation.yml` | PR + push to `main`/`master`/`enterprise-readiness`, manual | `windows-latest` ×3, `ubuntu-latest` ×3 | Build, unit tests, coverage gate, dependency CVE gate, secret scan, advisory static analysis, container build + boot smoke test, aggregated required check |
| `codeql.yml` | PR + push, weekly cron, manual | `windows-latest` (C#), `ubuntu-latest` (JS) | Deep security analysis with `security-extended` queries |
| `container-image.yml` | push to main branches + `v*` tags, manual | `ubuntu-latest` | Build → Trivy scan gate → multi-arch push to GHCR → SBOM + provenance attestation → optional cosign |
| `release-desktop.yml` | `v*.*.*` tag, manual | `windows-latest` ×2, `ubuntu-latest` ×1 | Version consistency gate → publish → sign → **Portable.zip + Inno Setup Setup.exe** → triple-layer invariant enforcement → draft release → verify → publish |
| `scheduled-maintenance.yml` | daily 04:00 UTC, manual | mixed | Published-image CVE sweep, NuGet advisory sweep, **yt-dlp drift canary**, **retroactive both-artifacts audit** |

### 2.1 `pr-validation.yml` — the PR gate

Six jobs plus an aggregator.

**`build-test`** (`windows-latest`) — Windows is **mandatory, not a preference**:
`TelegramAutoDownload.Tests` targets `net8.0-windows` and project-references the
WPF app (`TelegramAutoDownload.Tests.csproj:4,28`), so `ubuntu-latest` cannot even
restore it.

Steps: pin SDK 8 → assert the resolved SDK is 8.x → cache NuGet → **synthesise a
solution** → restore → build Release with a warnings-only MSBuild log uploaded as
an artifact → run unit tests with coverage → **coverage gate** → publish TRX via
`dorny/test-reporter`.

**`dependency-scan`** (`windows-latest`) — `dotnet list package --vulnerable
--include-transitive`, failing on any `High` or `Critical`. Deprecated packages are
reported but advisory. Runs on Windows because the restore must resolve the
`net8.0-windows` TFMs.

**`secret-scan`** (`ubuntu-latest`) — TruffleHog in two passes (verified findings
block; unverified are advisory), Gitleaks only when a licence secret is present,
plus a repo-specific tripwire for 32-hex Telegram `api_hash` values and numeric
`APP_ID`s. The tripwire explicitly allow-lists the documented placeholders in
`docker-compose.yml:20-21` so it does not fire on the shipped example.

**`static-analysis`** (`windows-latest`) — Roslyn analysers at
`AnalysisMode=Recommended` plus hadolint, both `continue-on-error`. **Advisory by
design:** the repo has no `.editorconfig` and no analyser baseline, so making this
blocking on day one would red-flag every PR with pre-existing findings. The job
emits a top-25 rule-frequency table so the baseline can be measured and then
ratcheted.

**`container-smoke`** (`ubuntu-latest`) — builds the image and **boots it**. This is
the highest-value job in the file: starting the container exercises schema
migrations (`Program.cs:105-108`), DI wiring for 12 registered services
(`Program.cs:28-40`), and `DownloadOrchestrator` startup. It then asserts that
`/data/tad.db` exists, that a rolling log file was created, that HTTP is served,
and that no `[FTL]` or unhandled exception appears in the logs. It probes
`/api/web-auth/status`, which is a public endpoint (`Program.cs:52`), accepting any
status `< 500` so the check survives auth-configuration changes.

**`pr-gate`** — aggregator. **Set this single check as the branch-protection
requirement**, not the individual jobs; that way adding or renaming a job does not
require editing branch protection.

### 2.2 The missing-solution problem, and how the workflows work around it

`README.md:111`, `:120`, and `:298` all instruct the reader to build and test
`RunnerApp.sln`. **That file does not exist** — `Get-ChildItem -Recurse -Filter
*.sln` returns nothing, tracked or untracked. So `dotnet build`/`dotnet test`
against the README's instructions fails today, and CodeQL's `autobuild` (which
looks for a solution) would too.

Every workflow that needs to compile therefore synthesises one in the runner
workspace:

```powershell
dotnet new sln -n TelegramAutoDownload-CI
Get-ChildItem -Recurse -Filter *.csproj |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { dotnet sln TelegramAutoDownload-CI.sln add $_.FullName }
```

This is a **workaround, not the fix.** It picks up all 11 projects
(`BasePlugins`, `DownloadPlugin`, `Logger`, `SocialMediaPlugin`,
`TelegramAutoDownload`, `TelegramAutoDownload.Headless`,
`TelegramAutoDownload.Tests`, `TelegramAutoDownload.UITests`, `TelegramClient`,
`TorrentPlugin`, `YoutubePlugin`) and nothing is committed. **Recommended follow-up
owned by the source team:** track a real `.sln` — either named `RunnerApp.sln` to
match the README, or renamed with the README updated — then delete the synthesis
steps from all four workflows.

### 2.3 Coverage collector risk — the most likely first-run failure

`TelegramAutoDownload.Tests.csproj:10-19` references `xunit`, `xunit.runner.visualstudio`,
`Microsoft.NET.Test.Sdk` 17.9.0, `FluentAssertions`, and `RichardSzalay.MockHttp`.
It does **not** reference `coverlet.collector`, which is the usual way a .NET repo
produces Cobertura output.

Because the constraints made every `*.csproj` read-only, the workflow instead uses
the **Microsoft.CodeCoverage** collector bundled with `Microsoft.NET.Test.Sdk`:

```
--collect:"Code Coverage;Format=cobertura"
```

This is Windows-only (fine — the job is Windows) and requires Test SDK 17.x (17.9.0
is present). If it does not emit a `*.cobertura.xml`, the gate step fails with an
explicit message pointing here. **The fix is a one-line csproj addition that this
change was not permitted to make:**

```xml
<PackageReference Include="coverlet.collector" Version="6.0.2">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

…then switch the test step to `--collect:"XPlat Code Coverage"`.

**The gate threshold is `COVERAGE_MINIMUM_LINE_PERCENT: "30"` and it is a guess.**
The real baseline is unknown because the suite has never been measured here. On the
first green run, read the actual number from the step summary and set the gate just
below it. **Ratchet-only: raise it, never lower it.**

### 2.4 UI tests are deliberately excluded from the gate

`TelegramAutoDownload.UITests` drives the WPF app through FlaUI/UIA3
(`TelegramAutoDownload.UITests.csproj:13`) and needs an interactive desktop
session. GitHub-hosted `windows-latest` runners can sometimes drive UIA, but the
results are flaky enough that including them would train the team to ignore red
builds. **Recommendation:** run them nightly on a **self-hosted Windows runner with
an interactive session** (see §4), and keep them out of the PR path.

### 2.5 `container-image.yml` — build, scan, publish

**Scan-before-push ordering is the point.** The job builds `linux/amd64` locally
with `load: true`, runs a blocking Trivy scan, and only then builds and pushes the
multi-arch manifest. A vulnerable image never reaches the registry.

Trivy is configured with `ignore-unfixed: true` on the blocking scan. This is a
considered trade-off: the runtime base is `mcr.microsoft.com/dotnet/aspnet:8.0`
(`Dockerfile:32`), a Debian image that always carries some advisories with no
available fix. Blocking on those would make every build red with no action
available, which is how scanners get disabled. A second non-blocking scan uploads
full-severity SARIF to code scanning so nothing is hidden.

**Tagging policy** is immutable-first: `sha-<short>` always, branch name,
`{{version}}` / `{{major}}.{{minor}}` / `{{major}}` on `v*` tags, and `latest` only
on the default branch. The job's summary prints a **digest-pinned** compose line to
paste, because `docker-compose.yml:6-7` currently uses `build: .` plus
`image: telegram-auto-download:latest` — neither of which is deployable in
production (see [`05-operations.md`](./05-operations.md) §4.1).

`linux/arm64` is included because the documented deployment is a NAS with Synology
bind-mount paths (`docker-compose.yml:26-28`). **This roughly doubles build time and
is the least-tested part of the pipeline** — the `Dockerfile` has never been built
for arm64. If the first run fails or times out, drop to `linux/amd64` and revisit.

SBOM (`anchore/sbom-action`, SPDX JSON) and build provenance
(`actions/attest-build-provenance`) are emitted. Cosign signing is present but
gated behind the repository variable `ENABLE_COSIGN=true`, because an unverified
signature provides no security value — turn it on when the deployment target
actually verifies.

### 2.6 `release-desktop.yml` — enforcing the both-artifacts rule

`.cursor/rules/release-process.mdc:10-15,33-36` makes it a hard project rule that
**every** release ships both `TelegramAutoDownload_vX.Y.Z_Setup.exe` and
`TelegramAutoDownload_vX.Y.Z_Portable.zip`, and explicitly forbids "fixing" an
update problem by deleting one of them.

This is not bureaucracy — it is load-bearing at runtime. `AutoUpdateService`
selects the asset by install type: Setup.exe for Program Files / LocalAppData
installs, the ZIP for portable installs (`Services/AutoUpdateService.cs:69-99`). If
only one asset is attached, the other half of the installed base falls back to the
wrong artifact (`:91-99`) and either installs to the wrong location or fails
entirely. And per [`05-operations.md`](./05-operations.md) §4.4, **rollback for each
install type requires that type's artifact to still exist on the previous release.**

#### Four enforcement layers

| Layer | Step | What it catches |
|---|---|---|
| **0. Version consistency** | "Resolve & verify version" | Tag, `AppVersion.cs:5`, and `setup.iss:2` must all agree. A stale `AppVersion.cs` would make every installed client believe an update is permanently available, because `IsNewer` compares the GitHub tag against the compiled constant (`AutoUpdateService.cs:66`, `:248-252`) |
| **1. Pre-publish** | "ENFORCE both-artifacts invariant (pre-publish)" | Both files exist, are named exactly per the rule, and are ≥20 MB (a self-contained single-file WPF publish cannot be smaller — a tiny file means a broken publish). Also re-checked after the artifact round-trip, with `sha256sum -c` |
| **2. Draft-first** | `gh release create --draft` | `AutoUpdateService` reads `/releases/latest` (`:24-25`), which **ignores drafts**. A run that dies midway can never be seen by a client |
| **3. Post-upload** | "ENFORCE both-artifacts invariant (post-upload)" | Re-queries GitHub for the attached asset names. If either is missing it **deletes the draft** (keeping the tag) and fails. Only then does `gh release edit --draft=false --latest` publish it. A `failure()` cleanup step is the final net |

A fifth, retroactive layer lives in `scheduled-maintenance.yml` → `release-audit`,
which re-checks the last 20 published releases daily and opens a hard failure if
any is missing an artifact.

#### Inno Setup on a hosted runner

`release-process.mdc:27` invokes `C:\InnoSetup\ISCC.exe`. GitHub-hosted runners have
no Inno Setup, so the workflow `choco install innosetup` and then resolves ISCC from
a candidate list — `C:\InnoSetup\ISCC.exe` first (so a self-hosted runner matching
the documented local layout works unchanged), then the Chocolatey defaults, then a
filesystem search. **It fails hard if ISCC is not found**, because
`release-process.mdc:36` forbids skipping the installer compile.

`setup.iss:6-8` sets `SourceDir=..\publish` and `OutputDir=..`, so running
`ISCC installer\setup.iss` from the repository root writes
`TelegramAutoDownload_v<version>_Setup.exe` to the root — which is exactly where the
invariant check and `gh release create` look.

#### Signing hooks

Two hooks: the application executable **before** it is zipped (so portable users get
a signed binary), and the installer **after** ISCC. Both use `signtool` with SHA-256
and an RFC-3161 timestamp, resolved from the newest Windows SDK on the runner. Both
are skipped when `SIGNING_CERT_BASE64` is unset, and an explicit warning is written
to the step summary so an unsigned release is never silent.

`actions/attest-build-provenance` runs regardless, and `SHA256SUMS.txt` is always
published — so even unsigned releases are verifiable by hash.

**Azure Trusted Signing is the better long-term option** (no PFX in secrets, no
certificate to rotate). Swap the two signing steps for
`azure/trusted-signing-action@v0` and replace the PFX secrets with the Azure
federated-credential set.

#### Release notes

Composed from three sources: a download table mapping each artifact to its audience,
the matching version section extracted from `CHANGELOG.md` when present, and
`--generate-notes` for the commit/PR list. Plus fixed upgrade/rollback guidance.

### 2.7 `scheduled-maintenance.yml` — the operational-burden automation

Four independent daily sweeps, each mapping to a named day-one risk:

1. **`base-image-cve`** — Trivy against the published `:latest`. CVEs land in a
   shipped image long after the commit that built it; nothing else would notice.
2. **`dependency-cve`** — NuGet advisories against pinned versions, opening or
   commenting on a single deduplicated GitHub issue.
3. **`ytdlp-canary`** — compares upstream `yt-dlp` against the version baked into
   the container image. This exists because the container installs `yt-dlp` from
   Debian apt (`Dockerfile:34-37`) and therefore **never updates**, while the
   desktop app self-updates on every launch
   (`TelegramAutoDownload/Services/YtdlpService.cs:41-108`). Container users hit
   site-change breakages that desktop users never see. The job files an issue with
   the remediation and the permanent fix. See
   [`05-operations.md`](./05-operations.md) runbook R6.
4. **`release-audit`** — retroactive enforcement of the both-artifacts rule across
   the last 20 releases, including an explicit reminder that deleting the other
   artifact is **not** the fix (`release-process.mdc:35`).

---

## 3. Required secrets, variables, and permissions

### 3.1 Secrets

| Secret | Required by | Needed for | If absent |
|---|---|---|---|
| `GITHUB_TOKEN` | all | GHCR push, `gh release`, issue creation, SARIF upload | Automatic — no action needed |
| `SIGNING_CERT_BASE64` | `release-desktop.yml` | Base64 of the Authenticode code-signing PFX | Signing steps skip; **artifacts ship unsigned** and a warning is written to the summary |
| `SIGNING_CERT_PASSWORD` | `release-desktop.yml` | PFX password | Same |
| `GITLEAKS_LICENSE` | `pr-validation.yml` | Gitleaks Action on an org-owned repo | Gitleaks step skips; TruffleHog still blocks on verified secrets |

**Optional, if switching to Azure Trusted Signing (recommended):**
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, plus
`TRUSTED_SIGNING_ENDPOINT` / account / profile names. Uses OIDC federated
credentials — no long-lived certificate material in GitHub at all.

**Optional, if the pipeline is later extended to deploy:** an SSH key or Tailscale
auth key for the target host, and `RESTIC_PASSWORD` if CI ever runs backup
verification. **Not implemented here** — deployment is intentionally left manual
until the health endpoints from [`05-operations.md`](./05-operations.md) §2.3 exist
to verify a rollout.

### 3.2 Repository variables

| Variable | Default | Effect |
|---|---|---|
| `ENABLE_COSIGN` | unset (off) | `true` enables keyless cosign signing of the container image |
| `SIGNING_TIMESTAMP_URL` | `http://timestamp.digicert.com` | RFC-3161 timestamp authority for `signtool` |

### 3.3 Workflow permissions

All workflows use least-privilege `permissions:` blocks rather than the repository
default:

| Workflow | Permissions | Why |
|---|---|---|
| `pr-validation.yml` | `contents: read` | Nothing is written |
| `codeql.yml` | `contents: read`, `security-events: write` | SARIF upload |
| `container-image.yml` | `contents: read`, `packages: write`, `id-token: write`, `attestations: write`, `security-events: write` | GHCR push, OIDC for attestation/cosign, SARIF |
| `release-desktop.yml` | `contents: write`, `id-token: write`, `attestations: write` | Create releases, attest artifacts |
| `scheduled-maintenance.yml` | `contents: read`, `issues: write`, `security-events: write`, `packages: read` | File issues, upload SARIF, pull the published image |

Two settings must be enabled in the repository **before** the workflows work:

1. **Settings → Actions → General → Workflow permissions** — must allow the
   per-workflow `permissions` blocks above (default `GITHUB_TOKEN` scope set to
   read-only is fine; the blocks escalate per workflow).
2. **Settings → Code security → Code scanning** — must be enabled for CodeQL and
   Trivy SARIF uploads to be accepted.

---

## 4. Where self-hosted runners are needed

Everything in this change targets GitHub-hosted runners, so the pipeline works from
the first push. Four cases justify self-hosting later:

| Case | Why | Requirement |
|---|---|---|
| **UI tests** (`TelegramAutoDownload.UITests`) | FlaUI/UIA3 (`UITests.csproj:13`) needs an interactive desktop | Windows runner configured as an **interactive user session**, not a service. Screen must not be locked |
| **Hardware-token code signing** | An EV certificate on a YubiKey/HSM cannot be exported into a GitHub secret | Windows runner with the token attached and drivers installed. *Avoidable* by using Azure Trusted Signing |
| **End-to-end Telegram tests** | Requires a real `session.dat` and API credentials. **Never put a live session in CI** — it is a bearer credential for the account (see [`05-operations.md`](./05-operations.md) §3.3), and two concurrent users of one auth key get it revoked | A dedicated runner with a **throwaway** test account, and its own session file that is never shared with production |
| **Deployment to the NAS/on-prem host** | The target is typically not internet-reachable | Runner inside the network, or a pull-based agent on the host |

One caution: a self-hosted Windows runner at the documented `C:\InnoSetup\ISCC.exe`
layout (`release-process.mdc:27`) would work with `release-desktop.yml` unchanged —
the ISCC resolution list checks that path **first**.

---

## 5. First-run validation checklist

Ordered by dependency: each item must pass before the next is meaningful. Nothing
below has been executed.

### Phase 0 — prerequisites (before pushing any workflow)

- [ ] **0.1** Decide on `global.json`. Recommendation and exact content in
      [`05-operations.md`](./05-operations.md) §6. **This change did not create it**
      — adding it changes build behaviour for everyone and breaks any machine
      without a .NET 8 SDK. Once added, switch every `setup-dotnet` step from
      `dotnet-version: "8.0.x"` to `global-json-file: global.json`.
- [ ] **0.2** Enable Code scanning in repository settings (§3.3).
- [ ] **0.3** Confirm the GHCR package namespace and that Actions may write packages.
- [ ] **0.4** Decide whether `il90il90` or this repository is the real release
      target. `AppVersion.GitHubOwner = "il90il90"` (`AppVersion.cs:6`) means builds
      from this repo auto-update from a third party's releases
      ([`05-operations.md`](./05-operations.md) §0.1b, U1). **Releases should not be
      cut until this is resolved.**

### Phase 1 — `pr-validation.yml`

- [ ] **1.1** Solution synthesis adds all 11 projects and `dotnet restore` succeeds.
      *Most likely failure:* a project that will not restore standalone.
- [ ] **1.2** `dotnet build` succeeds in Release on `windows-latest`. *Risk:* the
      WPF project or the `CopyPluginsToPublishDir` target
      (`TelegramAutoDownload.Headless.csproj:39-56`) may depend on build ordering
      the synthesised solution does not guarantee.
- [ ] **1.3** `dotnet test` runs and produces a TRX. Record **how many tests exist
      and how many pass** — that number is unknown right now.
- [ ] **1.4** **`--collect:"Code Coverage;Format=cobertura"` actually emits a
      `*.cobertura.xml`.** §2.3 — the single most likely failure in the whole
      pipeline. If it does not, add `coverlet.collector`.
- [ ] **1.5** Read the real coverage number from the step summary and set
      `COVERAGE_MINIMUM_LINE_PERCENT` just below it.
- [ ] **1.6** `dotnet list package --vulnerable` output format matches the
      `^\s*>\s.*\b(High|Critical)\b` regex. Verify by eye on the first run — the
      table format is not contractual across SDK versions.
- [ ] **1.7** TruffleHog `--results=verified --fail` does not false-positive on the
      documented placeholders (`docker-compose.yml:17-21`).
- [ ] **1.8** The credential tripwire's `grep -E` runs clean against the current tree.
- [ ] **1.9** **`container-smoke` boots.** Verify: HTTP answers, `/data/tad.db` is
      created, a rolling log appears, no `[FTL]`. *Risk:* the app may require
      `APP_ID`/`API_HASH` earlier than expected (`HeadlessHost.cs:48-54`), though it
      should only throw on `EnsureTelegramAsync`, not at startup. `WEB_AUTH_ENABLED=false`
      is set so the probe is unauthenticated (`WebAuthService.cs:50-57`).
- [ ] **1.10** `dorny/test-reporter@v1` can publish with `contents: read` only. If it
      cannot, either add `checks: write` or drop the step.
- [ ] **1.11** Set **`pr-gate`** as the only required status check in branch protection.
- [ ] **1.12** Measure the analyser warning count from the `static-analysis` summary
      and record it as the baseline to ratchet down.

### Phase 2 — `codeql.yml`

- [ ] **2.1** C# manual build mode completes within the 90-minute timeout.
- [ ] **2.2** `javascript-typescript` with `build-mode: none` finds the `wwwroot`
      assets and does not choke on vendored libraries.
- [ ] **2.3** Triage the first result set. Expect findings; classify before gating.

### Phase 3 — `container-image.yml`

- [ ] **3.1** GHCR login and push succeed; confirm the package appears and set its
      visibility deliberately.
- [ ] **3.2** Image name is fully lowercase (the normalisation step handles this,
      but confirm — GHCR rejects uppercase).
- [ ] **3.3** **Trivy does not block on unfixable base-image CVEs.** If the gate
      fires immediately, re-check `ignore-unfixed` and consider a `.trivyignore`
      with documented, time-boxed exceptions.
- [ ] **3.4** **`linux/arm64` builds.** Never attempted. The base images support it
      and `yt-dlp`/`ffmpeg` exist in Debian arm64, but expect a long build. If it
      fails, drop to `linux/amd64` and open an issue.
- [ ] **3.5** SBOM artifact is produced and non-empty.
- [ ] **3.6** `actions/attest-build-provenance` succeeds with
      `push-to-registry: true` (requires `id-token: write` and `packages: write`).
- [ ] **3.7** Pull the pushed digest on a clean host and boot it.

### Phase 4 — `release-desktop.yml` — **dry-run first, always**

- [ ] **4.1** Run via `workflow_dispatch` with `dry_run: true`. This builds and
      verifies everything but creates **no** release. **Do this before ever pushing
      a tag.**
- [ ] **4.2** Version extraction works: the regexes must match
      `AppVersion.cs:5` (`Current = "2.9.3"`) and `setup.iss:2`
      (`#define AppVersion "2.9.3"`). Both were checked by eye; neither was executed.
- [ ] **4.3** Confirm the mismatch gate **fails** when it should — dispatch with a
      wrong version and verify the run goes red.
- [ ] **4.4** `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`
      succeeds on the runner and emits `publish/TelegramAutoDownload.exe`.
- [ ] **4.5** **`publish/Plugins/` is populated.** The `CopyPluginsToPublishDir`
      target runs `AfterTargets="Publish"` and copies from each plugin's
      `bin/$(Configuration)/net8.0/` (`TelegramAutoDownload.Headless.csproj:39-56`).
      That target belongs to the **Headless** project — verify the equivalent exists
      for the WPF publish, because `setup.iss:60` packages `{#SourceDir}\Plugins\*`
      with `skipifsourcedoesntexist`, meaning **a missing Plugins folder produces a
      silently plugin-less installer.** The workflow warns; it cannot fail, because
      the correct behaviour here is unverified.
- [ ] **4.6** `choco install innosetup` succeeds and ISCC is found by the resolver.
- [ ] **4.7** ISCC compiles `installer/setup.iss` and writes
      `TelegramAutoDownload_v<version>_Setup.exe` to the repository root
      (`setup.iss:6-8`). The `SetupIconFile` dependency
      (`setup.iss:25` → `TelegramAutoDownload/044a51cf-2359-480c-8c20-bf372f8a4cab.ico`)
      **was verified to be tracked in git**, so it will be present on the runner.
- [ ] **4.8** The ≥20 MB size floor is correct for both artifacts. Adjust from the
      dry-run's measured sizes; the value is an estimate for a self-contained
      single-file .NET 8 WPF build.
- [ ] **4.9** With signing secrets configured: `signtool` is found, both artifacts
      are signed, and `signtool verify /pa` passes.
- [ ] **4.10** Without signing secrets: the run still succeeds and the unsigned
      warning appears in the summary.
- [ ] **4.11** `SHA256SUMS.txt` is generated and `sha256sum -c` passes in the
      `publish` job after the artifact round-trip.
- [ ] **4.12** **End-to-end on a throwaway tag** (e.g. `v0.0.1-ci-test` — note the
      trigger is `v*.*.*`, so use a real-looking version in a scratch repository or
      fork). Verify the draft is created, the post-upload check passes, and the
      release is un-drafted with **both** assets.
- [ ] **4.13** **Verify the failure path.** Temporarily break the ZIP step and
      confirm the run fails **and** deletes the draft, leaving no half-published
      release. This is the single most important check in the file — the whole
      value of the workflow is that it cannot ship a one-artifact release.
- [ ] **4.14** Confirm `--verify-tag` behaves as expected for `workflow_dispatch`
      runs, where the tag may not exist yet. **This is a known open question:**
      `--verify-tag` requires the tag to exist; a dispatch-triggered release may
      need the flag dropped or the tag created first.
- [ ] **4.15** Confirm the CHANGELOG `awk` extraction produces the expected text.
      The real format was checked — `CHANGELOG.md` uses `## [2.9.3] - 2026-05-24`,
      which the pattern `^#+ *\[?v?<ver>\]?` matches, terminating at the next
      version heading. Sub-headings (`### Added`) are correctly kept inside the
      section. A mismatch is non-fatal: the section is simply omitted.

### Phase 5 — `scheduled-maintenance.yml`

- [ ] **5.1** `ytdlp-canary` can pull the published image and run `yt-dlp --version`
      inside it. *Risk:* the `--entrypoint sh` override plus the non-root user
      (`docker-compose.yml:11` is compose-only, but the image itself has no `USER`
      directive, so this should be fine) — confirm.
- [ ] **5.2** `release-audit` handles a repository with **zero** releases without
      failing spuriously.
- [ ] **5.3** Issue creation works and dedupes correctly on the second day.
- [ ] **5.4** Confirm the `security` and `ops` labels exist, or `gh issue create`
      fails. Create them, or remove the `--label` flags.

---

## 6. Known open questions

Things that could not be settled without executing the pipelines or touching source:

1. **Coverage collector** — §2.3. Highest-probability first-run failure.
2. **Coverage threshold** — `30` is a placeholder; the real baseline is unmeasured.
3. **`--verify-tag` on `workflow_dispatch`** — checklist 4.14.
4. **WPF publish and the `Plugins/` folder** — checklist 4.5. If plugins are not
   copied for the desktop publish, releases have been shipping without plugin DLLs,
   or there is a mechanism outside the two csproj files examined. Worth a deliberate
   check before the next release.
5. **arm64 image** — never built; may fail or be slow.
6. **Test count and pass rate** — unknown. 30 test files exist under
   `TelegramAutoDownload.Tests/`; how many pass today is not established.
7. **Whether a `.sln` should be tracked** — recommended (§2.2), but it is a source
   change.
8. **Release target repository** — checklist 0.4. This is a security decision, not a
   CI one, and it blocks safe releases.

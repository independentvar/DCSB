# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Deathcounter and Soundboard (DCSB) — a .NET 10 (`net10.0-windows`) WPF app using CommunityToolkit.Mvvm. Every project targets `net10.0-windows`; the app is **framework-dependent**, so end users need the .NET 10 Desktop Runtime (x64) installed (the installer checks for it — see Gotchas). SDK-style csproj files (`<Project Sdk="Microsoft.NET.Sdk">`) with `PackageReference` NuGet — no `packages.config`, no `packages\` folder; packages restore to the global NuGet cache. Restore happens automatically on `dotnet build`. A repo-root `Directory.Build.props` sets `AppendTargetFrameworkToOutputPath=false` so every project still outputs to `bin\<Config>\` (no `net10.0-windows\` subfolder), keeping the installer and CI paths stable. `DCSB\DCSB.csproj` is the entry exe; the other projects are layers (Models / ViewModels / Views / Business / Input / Sound / Utils / etc.). MSTest tests live in `DCSB.Tests` (covering ConfigurationManager, config serialization, and UpdateManager version parsing).

## Build and release

- Build: `dotnet build DCSB.sln -c Release` (restores automatically). Needs the .NET 10 SDK; CI installs it via `actions/setup-dotnet` — see `.github/workflows/build-release.yml`.
- Tests: `dotnet test DCSB.Tests\DCSB.Tests.csproj -c Release`. `DCSB.Tests` runs under **Microsoft.Testing.Platform** (`EnableMSTestRunner`/`OutputType=Exe`), so the build produces a self-launching `DCSB.Tests.exe` whose exit code and console summary reflect the result — CI runs that exe directly (the classic `vstest.console.exe` path no longer resolves this project's deps and should not be used). Note `dotnet test` prints little to a redirected pipe; run `DCSB.Tests\bin\Release\DCSB.Tests.exe` for visible output.
- UI/integration tests: `DCSB.UITests\Run-UITests.ps1` drives the built Release exe via UI Automation and meters real audio output on the endpoints. Manual only — needs an interactive desktop session and audio devices; not part of the sln, never run in CI. See `DCSB.UITests\README.md`.
- CI builds every push and PR. PRs into `master` require the `build` check (branch protection). PR runs for same-repo branches are deduplicated: the pull_request-event job is skipped and the push-event run provides the real result.
- **Releasing is automatic**: merging to `master` builds the NSIS installer and publishes a GitHub release tagged `v<version>` from `AssemblyVersionInfo.cs`. Pushing the same version twice updates the existing release instead of creating a new one — bump the version to get a new release. A push whose files all live under `DCSB.Tests/` or `DCSB.UITests/` still builds and runs tests but skips the release step entirely (see the `Detect test-only change` step in `build-release.yml`), so test-only merges never touch a release.
- `AssemblyVersionInfo.cs` at the repo root is link-included into every project; it is the single source of the app version and the release tag.
- **Every PR that changes shipped code must bump the version in `AssemblyVersionInfo.cs` in that same PR** (update both `AssemblyVersion` and `AssemblyFileVersion`; increment the third component for fixes, the second for features). Merging shipped-code changes without a bump silently overwrites the existing release instead of publishing a new one. Only skip the bump for changes that don't affect the shipped app (CI, docs, tests-only) — and note that tests-only merges skip the release step altogether, so they never touch it either way.

## Gotchas

- The installer's `File`/`Delete` lists are **generated** from `DCSB\bin\Release\` by `Installer scripts\GenerateInstallerIncludes.ps1` (run via `!system` at the top of `CreateInstaller.nsi`), so adding/removing a dependency needs no manual edit — but you must build Release first, and the generator only ships `.dll`/`.exe`/`.config`/`.json`. The `.json` matters on .NET: the apphost `DCSB.exe` needs `DCSB.deps.json` and `DCSB.runtimeconfig.json` beside it to find the shared runtime. Because the app is framework-dependent, `CreateInstaller.nsi`'s `.onInit` checks for a `Microsoft.WindowsDesktop.App\10.*` folder and offers the runtime download page if it's missing.
- Folder `DCSB.Sound\` builds project `DCSB.SoundPlayer.csproj` → `DCSB.SoundPlayer.dll` (folder and assembly names differ).
- Each project keeps its hand-written `Properties\AssemblyInfo.cs`; the SDK csproj sets `GenerateAssemblyInfo=false` to avoid duplicate-attribute clashes with those files and the link-included `AssemblyVersionInfo.cs`.
- User config is stored machine-wide at `%ProgramData%\DCSB\config.xml`. Saves are debounced 1s via a timer in `DCSB.Business\ConfigurationManager.cs`; `Dispose()` flushes the pending save.
- The update checker (`DCSB.Business\UpdateManager.cs`) reads this fork's GitHub releases and parses the version from the tag name — tags must contain `x.y.z.w`. TLS 1.2 is enabled explicitly there (harmless leftover from the .NET 4.5.2 days; modern .NET negotiates TLS 1.2+ by default). It still uses the obsolete `WebClient`/`ServicePointManager` APIs, which build with `SYSLIB0014` warnings on .NET 10.
- `DCSB.Input` uses Win32 Raw Input P/Invoke for global hotkeys — sensitive to window-handle lifecycle.
- `ThirdParty\rnnoise\rnnoise.dll` is a **committed native binary** (mic noise suppression, P/Invoked by `DCSB.SoundPlayer.NoiseSuppressor`). It is never built locally: the `build-rnnoise` GitHub Actions workflow compiles it from a pinned xiph/rnnoise tag via `ThirdParty\rnnoise\build-rnnoise.ps1` (pins + provenance in `ThirdParty\rnnoise\README.md`). `DCSB.SoundPlayer.csproj` copies it to output, so the installer picks it up automatically.

## Repo etiquette

- Default branch is `master` (not `main`). Work on `dev/<topic>` branches and merge via PR; direct pushes to `master` are for admins only.
- `gh` commands: the repo is a fork of Kalejin/DCSB — always target `independentvar/DCSB` (set as default via `gh repo set-default`), never open PRs against the upstream repo.

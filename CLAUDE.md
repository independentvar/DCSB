# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Deathcounter and Soundboard (DCSB) — a .NET Framework 4.8.1 WPF app using MVVM Light. Old-style csproj files with packages.config NuGet (packages restore to the repo-root `packages\` folder via HintPath; run `nuget restore DCSB.sln` before building a fresh clone). `DCSB\DCSB.csproj` is the entry exe; the other projects are layers (Models / ViewModels / Views / Business / Input / Sound / Utils / etc.). MSTest tests live in `DCSB.Tests` (covering ConfigurationManager, config serialization, and UpdateManager version parsing).

## Build and release

- Build: `msbuild DCSB.sln /p:Configuration=Release` (requires the .NET Framework 4.8.1 targeting pack; CI installs it from the `Microsoft.NETFramework.ReferenceAssemblies.net481` NuGet package — see `.github/workflows/build-release.yml`).
- Tests: run with `vstest.console.exe DCSB.Tests\bin\Release\DCSB.Tests.dll /TestAdapterPath:packages\MSTest.TestAdapter.2.2.10\build\_common` (CI locates vstest via vswhere; if VS isn't installed locally, extract the `Microsoft.TestPlatform` NuGet package and use its `tools\net462\...\vstest.console.exe`). CI runs tests on every build.
- CI builds every push and PR. PRs into `master` require the `build` check (branch protection). PR runs for same-repo branches are deduplicated: the pull_request-event job is skipped and the push-event run provides the real result.
- **Releasing is automatic**: merging to `master` builds the NSIS installer and publishes a GitHub release tagged `v<version>` from `AssemblyVersionInfo.cs`. Pushing the same version twice updates the existing release instead of creating a new one — bump the version to get a new release.
- `AssemblyVersionInfo.cs` at the repo root is link-included into every project; it is the single source of the app version and the release tag.
- **Every PR that changes shipped code must bump the version in `AssemblyVersionInfo.cs` in that same PR** (update both `AssemblyVersion` and `AssemblyFileVersion`; increment the third component for fixes, the second for features). Merging without a bump silently overwrites the existing release instead of publishing a new one. Only skip the bump for changes that don't affect the shipped app (CI, docs, tests-only).

## Gotchas

- **Adding/removing a DLL dependency requires editing `Installer scripts\CreateInstaller.nsi`** — the installer (and its uninstall section) lists every shipped file explicitly. CI copies `NVorbis.dll` into the output manually (workflow step) because MSBuild won't copy it transitively.
- Folder `DCSB.Sound\` builds project `DCSB.SoundPlayer.csproj` → `DCSB.SoundPlayer.dll` (folder and assembly names differ).
- `DCSB.csproj` contains stale ClickOnce properties (PublishUrl, kalejin.eu UpdateUrl, etc.) — legacy, unrelated to the NSIS installer; ignore them.
- User config is stored machine-wide at `%ProgramData%\DCSB\config.xml`. Saves are debounced 1s via a timer in `DCSB.Business\ConfigurationManager.cs`; `Dispose()` flushes the pending save.
- The update checker (`DCSB.Business\UpdateManager.cs`) reads this fork's GitHub releases and parses the version from the tag name — tags must contain `x.y.z.w`. TLS 1.2 is enabled explicitly there (harmless leftover from the .NET 4.5.2 days; .NET 4.8.1 enables it by default).
- `DCSB.Input` uses Win32 Raw Input P/Invoke for global hotkeys — sensitive to window-handle lifecycle.

## Repo etiquette

- Default branch is `master` (not `main`). Work on `dev/<topic>` branches and merge via PR; direct pushes to `master` are for admins only.
- `gh` commands: the repo is a fork of Kalejin/DCSB — always target `independentvar/DCSB` (set as default via `gh repo set-default`), never open PRs against the upstream repo.

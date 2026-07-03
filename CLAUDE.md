# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Deathcounter and Soundboard (DCSB) — a .NET Framework 4.5.2 WPF app using MVVM Light. Old-style csproj files with packages.config NuGet (packages restore to the repo-root `packages\` folder via HintPath; run `nuget restore DCSB.sln` before building a fresh clone). `DCSB\DCSB.csproj` is the entry exe; the other projects are layers (Models / ViewModels / Views / Business / Input / Sound / Utils / etc.). There are no tests.

## Build and release

- Build: `msbuild DCSB.sln /p:Configuration=Release` (requires the .NET Framework 4.5.2 targeting pack; CI installs it from the `Microsoft.NETFramework.ReferenceAssemblies.net452` NuGet package — see `.github/workflows/build-release.yml`).
- CI builds every push and PR. PRs into `master` require the `build` check (branch protection). PR runs for same-repo branches are deduplicated: the pull_request-event job is skipped and the push-event run provides the real result.
- **Releasing is automatic**: merging to `master` builds the NSIS installer and publishes a GitHub release tagged `v<version>` from `AssemblyVersionInfo.cs`. Pushing the same version twice updates the existing release instead of creating a new one — bump the version to get a new release.
- `AssemblyVersionInfo.cs` at the repo root is link-included into every project; it is the single source of the app version and the release tag.

## Gotchas

- **Adding/removing a DLL dependency requires editing `Installer scripts\CreateInstaller.nsi`** — the installer (and its uninstall section) lists every shipped file explicitly. CI copies `NVorbis.dll` into the output manually (workflow step) because MSBuild won't copy it transitively.
- Folder `DCSB.Sound\` builds project `DCSB.SoundPlayer.csproj` → `DCSB.SoundPlayer.dll` (folder and assembly names differ).
- `DCSB.csproj` contains stale ClickOnce properties (PublishUrl, kalejin.eu UpdateUrl, etc.) — legacy, unrelated to the NSIS installer; ignore them.
- User config is stored machine-wide at `%ProgramData%\DCSB\config.xml`. Saves are debounced 1s via a timer in `DCSB.Business\ConfigurationManager.cs`; `Dispose()` flushes the pending save.
- The update checker (`DCSB.Business\UpdateManager.cs`) reads this fork's GitHub releases and parses the version from the tag name — tags must contain `x.y.z.w`. TLS 1.2 is enabled explicitly there; .NET 4.5.2 defaults would fail against GitHub.
- `DCSB.Input` uses Win32 Raw Input P/Invoke for global hotkeys — sensitive to window-handle lifecycle.

## Repo etiquette

- Default branch is `master` (not `main`). Work on `dev/<topic>` branches and merge via PR; direct pushes to `master` are for admins only.
- `gh` commands: the repo is a fork of Kalejin/DCSB — always target `independentvar/DCSB` (set as default via `gh repo set-default`), never open PRs against the upstream repo.

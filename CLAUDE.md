# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Deathcounter and Soundboard (DCSB) — a .NET Framework 4.8.1 WPF app using CommunityToolkit.Mvvm. SDK-style csproj files (`<Project Sdk="Microsoft.NET.Sdk">`) with `PackageReference` NuGet — no `packages.config`, no `packages\` folder; packages restore to the global NuGet cache. Restore happens automatically on `dotnet build`; with VS/`msbuild` run `msbuild DCSB.sln -t:restore` first (or add `-restore`). A repo-root `Directory.Build.props` sets `AppendTargetFrameworkToOutputPath=false` so every project still outputs to `bin\<Config>\` (no `net481\` subfolder), keeping the installer and CI paths stable. `DCSB\DCSB.csproj` is the entry exe; the other projects are layers (Models / ViewModels / Views / Business / Input / Sound / Utils / etc.). MSTest tests live in `DCSB.Tests` (covering ConfigurationManager, config serialization, and UpdateManager version parsing).

## Build and release

- Build: `dotnet build DCSB.sln -c Release` (restores automatically), or `msbuild DCSB.sln -t:restore,build /p:Configuration=Release`. Building net481 requires the .NET Framework 4.8.1 targeting pack; CI installs it from the `Microsoft.NETFramework.ReferenceAssemblies.net481` NuGet package — see `.github/workflows/build-release.yml`.
- Tests: `dotnet test DCSB.Tests\DCSB.Tests.csproj -c Release`, or via vstest: `vstest.console.exe DCSB.Tests\bin\Release\DCSB.Tests.dll /TestAdapterPath:DCSB.Tests\bin\Release` (the MSTest adapter DLLs are copied next to the test assembly by `PackageReference`). CI locates vstest via vswhere and runs tests on every build.
- UI/integration tests: `DCSB.UITests\Run-UITests.ps1` drives the built Release exe via UI Automation and meters real audio output on the endpoints. Manual only — needs an interactive desktop session and audio devices; not part of the sln, never run in CI. See `DCSB.UITests\README.md`.
- CI builds every push and PR. PRs into `master` require the `build` check (branch protection). PR runs for same-repo branches are deduplicated: the pull_request-event job is skipped and the push-event run provides the real result.
- **Releasing is automatic**: merging to `master` builds the NSIS installer and publishes a GitHub release tagged `v<version>` from `AssemblyVersionInfo.cs`. Pushing the same version twice updates the existing release instead of creating a new one — bump the version to get a new release. A push whose files all live under `DCSB.Tests/` or `DCSB.UITests/` still builds and runs tests but skips the release step entirely (see the `Detect test-only change` step in `build-release.yml`), so test-only merges never touch a release.
- `AssemblyVersionInfo.cs` at the repo root is link-included into every project; it is the single source of the app version and the release tag.
- **Every PR that changes shipped code must bump the version in `AssemblyVersionInfo.cs` in that same PR** (update both `AssemblyVersion` and `AssemblyFileVersion`; increment the third component for fixes, the second for features). Merging shipped-code changes without a bump silently overwrites the existing release instead of publishing a new one. Only skip the bump for changes that don't affect the shipped app (CI, docs, tests-only) — and note that tests-only merges skip the release step altogether, so they never touch it either way.

## Gotchas

- **Adding/removing a DLL dependency requires editing `Installer scripts\CreateInstaller.nsi`** — the installer (and its uninstall section) lists every shipped file explicitly. After the SDK-style migration, `PackageReference` copies all transitive dependencies (including `NVorbis.dll`) into the app output, so no manual copy step is needed; verify the DLL set in `DCSB\bin\Release\` still matches the installer's `File`/`Delete` lists.
- Folder `DCSB.Sound\` builds project `DCSB.SoundPlayer.csproj` → `DCSB.SoundPlayer.dll` (folder and assembly names differ).
- Each project keeps its hand-written `Properties\AssemblyInfo.cs`; the SDK csproj sets `GenerateAssemblyInfo=false` to avoid duplicate-attribute clashes with those files and the link-included `AssemblyVersionInfo.cs`.
- User config is stored machine-wide at `%ProgramData%\DCSB\config.xml`. Saves are debounced 1s via a timer in `DCSB.Business\ConfigurationManager.cs`; `Dispose()` flushes the pending save.
- The update checker (`DCSB.Business\UpdateManager.cs`) reads this fork's GitHub releases and parses the version from the tag name — tags must contain `x.y.z.w`. TLS 1.2 is enabled explicitly there (harmless leftover from the .NET 4.5.2 days; .NET 4.8.1 enables it by default).
- `DCSB.Input` uses Win32 Raw Input P/Invoke for global hotkeys — sensitive to window-handle lifecycle.

## Repo etiquette

- Default branch is `master` (not `main`). Work on `dev/<topic>` branches and merge via PR; direct pushes to `master` are for admins only.
- `gh` commands: the repo is a fork of Kalejin/DCSB — always target `independentvar/DCSB` (set as default via `gh repo set-default`), never open PRs against the upstream repo.

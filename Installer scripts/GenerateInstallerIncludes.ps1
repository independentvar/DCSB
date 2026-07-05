# Generates the NSIS include fragments that CreateInstaller.nsi depends on, straight
# from the built app output so nothing drifts from what actually ships:
#   App-Version.txt    - !define Version, read from DCSB.exe's file version
#   Files-Install.nsi  - the File (install) list
#   Files-Uninstall.nsi - the Delete (uninstall) list
# Invoked from CreateInstaller.nsi via !system; idempotent, so re-running is harmless.
$ErrorActionPreference = 'Stop'
$buildDir = Join-Path $PSScriptRoot '..\DCSB\bin\Release'

if (-not (Test-Path $buildDir)) {
    throw "Build output not found at '$buildDir'. Build DCSB (Release) before generating installer includes."
}

# --- Version: read from the built exe (same source the CI release tag uses) ---
$exe = Join-Path $buildDir 'DCSB.exe'
if (-not (Test-Path $exe)) {
    throw "DCSB.exe not found at '$exe'."
}
$version = (Get-Item $exe).VersionInfo.FileVersion
Set-Content -Path (Join-Path $PSScriptRoot 'App-Version.txt') `
    -Value "!define Version `"$version`"" -Encoding ASCII

# --- File lists: ship exactly what the app needs to run (assemblies, exe, config) ---
# (No .pdb / .xml doc files - matches the previous hand-maintained list.)
$files = Get-ChildItem -Path $buildDir -File |
    Where-Object { $_.Extension -in '.dll', '.exe', '.config' } |
    Sort-Object Name          # sorted => stable, reviewable diffs

if (-not $files) {
    throw "No shippable files (.dll/.exe/.config) found in '$buildDir'."
}

$install   = $files | ForEach-Object { "    File `"..\DCSB\bin\Release\$($_.Name)`"" }
$uninstall = $files | ForEach-Object { "    Delete `"`$INSTDIR\$($_.Name)`"" }

Set-Content -Path (Join-Path $PSScriptRoot 'Files-Install.nsi')   -Value $install   -Encoding ASCII
Set-Content -Path (Join-Path $PSScriptRoot 'Files-Uninstall.nsi') -Value $uninstall -Encoding ASCII

Write-Host "Generated App-Version.txt (v$version) and file lists ($($files.Count) files)."

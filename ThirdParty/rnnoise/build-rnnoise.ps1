# Builds rnnoise.dll (x64) from a pinned tag of https://github.com/xiph/rnnoise.
#
# This is the canonical way to (re)produce the rnnoise.dll committed in this
# folder. It is normally run by the build-rnnoise GitHub Actions workflow
# (Actions -> "Build rnnoise.dll" -> Run workflow), so no developer machine
# needs a C toolchain; it also runs locally on any machine with MSVC
# (VC++ x64 tools + Windows SDK) installed.
#
# To upgrade rnnoise: bump $Tag/$Commit, run download_model.sh's URL by hand to
# get the new model hash (or run this script once and read the mismatch error),
# update $ModelSha256, dispatch the workflow, commit the new dll and update
# README.md with the printed SHA-256.
#
# Requires: git, tar, curl (all present on GitHub windows runners and in
# Git for Windows), and Visual Studio 2022+ with the VC++ x64 toolset.

param(
    # upstream tag to build, and the commit it must resolve to (tags are movable;
    # the commit pin is what actually guarantees reproducibility)
    [string]$Tag = 'v0.2',
    [string]$Commit = '904a876dce1f9ab8860c0a5000ed151f9f6eef58',
    # SHA-256 of the model-weights tarball referenced by the tag's model_version
    # file (upstream pins the version in-tree but serves the data from
    # media.xiph.org, so the content hash is pinned here)
    [string]$ModelSha256 = '4ac81c5c0884ec4bd5907026aaae16209b7b76cd9d7f71af582094a2f98f4b43',
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$work = Join-Path ([IO.Path]::GetTempPath()) "rnnoise-build-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # ---- fetch pinned source ----
    git clone --quiet --depth 1 --branch $Tag https://github.com/xiph/rnnoise (Join-Path $work 'src-tree')
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
    $tree = Join-Path $work 'src-tree'
    $head = (git -C $tree rev-parse HEAD).Trim()
    if ($head -ne $Commit) { throw "tag $Tag resolved to $head, expected $Commit - refusing to build" }

    # ---- fetch pinned model weights (what download_model.sh does, hash-checked) ----
    $modelVersion = (Get-Content (Join-Path $tree 'model_version') -Raw).Trim()
    $modelFile = "rnnoise_data-$modelVersion.tar.gz"
    $modelPath = Join-Path $work $modelFile
    curl.exe -sSL -o $modelPath "https://media.xiph.org/rnnoise/models/$modelFile"
    if ($LASTEXITCODE -ne 0) { throw 'model download failed' }
    $actualHash = (Get-FileHash $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $ModelSha256) { throw "model tarball hash mismatch: got $actualHash, expected $ModelSha256" }
    tar -xomf $modelPath -C $tree
    if ($LASTEXITCODE -ne 0) { throw 'model extraction failed' }
    if (-not (Test-Path (Join-Path $tree 'src\rnnoise_data.c'))) { throw 'model tarball did not contain src/rnnoise_data.c' }

    # ---- locate MSVC (x64 host/target) ----
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found - install Visual Studio or Build Tools' }
    $vsRoot = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsRoot) { throw 'no Visual Studio with the VC++ x64 toolset found' }
    $vcvars = Join-Path $vsRoot 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found under $vsRoot" }

    # ---- compile ----
    # source list mirrors RNNOISE_SOURCES in upstream's Makefile.am (generic C
    # path; the optional x86 runtime-dispatch SSE4.1/AVX2 variants are skipped
    # for simplicity - rnnoise is comfortably real-time without them).
    # WIN32+RNNOISE_BUILD+DLL_EXPORT make rnnoise.h emit __declspec(dllexport).
    # __SSE2__ is a GCC-style macro MSVC never defines: without it vec.h falls
    # into a generic branch that includes os_support.h, a file the tarball does
    # not ship (upstream only builds with gcc/clang). Every x64 CPU has SSE2, so
    # defining it selects the vec_avx.h intrinsics path upstream actually uses.
    $sources = 'denoise.c', 'rnn.c', 'pitch.c', 'kiss_fft.c', 'celt_lpc.c',
               'nnet.c', 'nnet_default.c', 'parse_lpcnet_weights.c',
               'rnnoise_data.c', 'rnnoise_tables.c' |
               ForEach-Object { "src\$_" }
    $build = @(
        "call `"$vcvars`"",
        "if errorlevel 1 exit /b 1",
        "cd /d `"$tree`"",
        "cl /nologo /O2 /LD /DWIN32 /DRNNOISE_BUILD /DDLL_EXPORT /D__SSE2__ /Iinclude /Isrc $($sources -join ' ') /Fe:rnnoise.dll"
    ) -join "`r`n"
    $buildCmd = Join-Path $work 'build.cmd'
    Set-Content -Path $buildCmd -Value $build -Encoding ascii
    cmd /c $buildCmd
    if ($LASTEXITCODE -ne 0) { throw "cl.exe failed with exit code $LASTEXITCODE" }

    # ---- publish ----
    $dll = Join-Path $tree 'rnnoise.dll'
    if (-not (Test-Path $dll)) { throw 'rnnoise.dll was not produced' }
    Copy-Item $dll (Join-Path $OutDir 'rnnoise.dll') -Force
    Copy-Item (Join-Path $tree 'COPYING') (Join-Path $OutDir 'COPYING') -Force

    $dllHash = (Get-FileHash (Join-Path $OutDir 'rnnoise.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ''
    Write-Host "rnnoise.dll built from $Tag ($Commit)"
    Write-Host "model tarball: $modelFile ($ModelSha256)"
    Write-Host "rnnoise.dll SHA-256: $dllHash"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

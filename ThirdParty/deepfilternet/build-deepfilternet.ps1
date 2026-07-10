# Builds deepfilter.dll (x64) and fetches the DeepFilterNet3 model from a pinned
# tag of https://github.com/Rikorose/DeepFilterNet.
#
# Same arrangement as ..\rnnoise: this script is the canonical way to
# (re)produce the committed binaries, normally run by the build-deepfilternet
# GitHub Actions workflow (Actions -> "Build deepfilter.dll" -> Run workflow),
# so no developer machine needs a Rust toolchain. The dll is libDF's own C API
# (df_create/df_get_frame_length/df_process_frame/df_free) compiled as a
# cdylib; the model tar.gz is taken from the same pinned checkout and is what
# df_create loads at runtime.
#
# To upgrade DeepFilterNet: bump $Tag/$Commit (and $RustToolchain if the newer
# code needs it), dispatch the workflow, commit the new dll + model and update
# README.md with the printed SHA-256 hashes.
#
# Requires: git and rustup (both present on GitHub windows runners).

param(
    # upstream tag to build, and the commit it must resolve to
    [string]$Tag = 'v0.5.6',
    [string]$Commit = '978576aa8400552a4ce9730838c635aa30db5e61',
    # Rust toolchain pinned for reproducibility; era-appropriate for the tag and
    # well above libDF's rust-version = 1.60 floor
    [string]$RustToolchain = '1.75.0',
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$work = Join-Path ([IO.Path]::GetTempPath()) "deepfilternet-build-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    # ---- fetch pinned source ----
    git clone --quiet --depth 1 --branch $Tag https://github.com/Rikorose/DeepFilterNet (Join-Path $work 'src-tree')
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
    $tree = Join-Path $work 'src-tree'
    $head = (git -C $tree rev-parse HEAD).Trim()
    if ($head -ne $Commit) { throw "tag $Tag resolved to $head, expected $Commit - refusing to build" }

    # ---- pinned toolchain ----
    rustup toolchain install $RustToolchain --profile minimal --no-self-update
    if ($LASTEXITCODE -ne 0) { throw 'rustup toolchain install failed' }

    # ---- compile the C API as a self-contained cdylib ----
    # +crt-static so the dll doesn't require the VC++ redistributable on user
    # machines; --locked builds with the repo's committed Cargo.lock so the
    # dependency graph is exactly what upstream shipped at the tag
    $env:RUSTFLAGS = '-C target-feature=+crt-static'
    Push-Location $tree
    try {
        cargo "+$RustToolchain" rustc -p deep_filter --release --locked --features capi --crate-type cdylib
        if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }
    } finally {
        Pop-Location
        Remove-Item Env:\RUSTFLAGS -ErrorAction SilentlyContinue
    }

    # ---- publish (lib target is named "df"; ship under the upstream capi name) ----
    $dll = Join-Path $tree 'target\release\df.dll'
    if (-not (Test-Path $dll)) { throw 'df.dll was not produced' }
    Copy-Item $dll (Join-Path $OutDir 'deepfilter.dll') -Force
    Copy-Item (Join-Path $tree 'models\DeepFilterNet3_onnx.tar.gz') (Join-Path $OutDir 'DeepFilterNet3_onnx.tar.gz') -Force
    Copy-Item (Join-Path $tree 'LICENSE-MIT') (Join-Path $OutDir 'LICENSE-MIT') -Force
    Copy-Item (Join-Path $tree 'LICENSE-APACHE') (Join-Path $OutDir 'LICENSE-APACHE') -Force

    $dllHash = (Get-FileHash (Join-Path $OutDir 'deepfilter.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    $modelHash = (Get-FileHash (Join-Path $OutDir 'DeepFilterNet3_onnx.tar.gz') -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ''
    Write-Host "deepfilter.dll built from $Tag ($Commit) with Rust $RustToolchain"
    Write-Host "deepfilter.dll SHA-256: $dllHash"
    Write-Host "DeepFilterNet3_onnx.tar.gz SHA-256: $modelHash"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

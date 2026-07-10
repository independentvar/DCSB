# Vendored DeepFilterNet3 (deep-learning noise suppression)

`deepfilter.dll` (x64) is libDF's C API compiled from
[Rikorose/DeepFilterNet](https://github.com/Rikorose/DeepFilterNet), dual-licensed
MIT/Apache-2.0 (see `LICENSE-MIT` / `LICENSE-APACHE`). It powers the microphone's
"High quality" noise suppression mode (`DCSB.SoundPlayer.DeepFilterNetSuppressor`
P/Invokes it) and is copied to the app output by `DCSB.SoundPlayer.csproj`, from
where the installer ships it like any other dll.

`DeepFilterNet3_onnx.tar.gz` is the model-weights archive from the same pinned
checkout. libDF's `df_create` only accepts a file path, and the installer ships
no loose non-dll files, so the csproj embeds it into `DCSB.SoundPlayer.dll` as a
resource and the suppressor extracts it to a temp file at first use (same
pattern as the wizard's `beep.wav`).

## Provenance of the committed binaries

| | |
|---|---|
| Upstream tag | `v0.5.6` |
| Upstream commit | `978576aa8400552a4ce9730838c635aa30db5e61` |
| Rust toolchain | `1.75.0`, `--locked` against upstream's Cargo.lock, `+crt-static` |
| Built by | `build-deepfilternet` GitHub Actions workflow (windows-latest), run 29124643754 |
| `deepfilter.dll` SHA-256 | `ac58fcb4ee96e2524b809ebbcf68f04ede82384e4ebc4f77073606f699788745` |
| `DeepFilterNet3_onnx.tar.gz` SHA-256 | `c94d91f70911001c946e0fabb4aa9adc37045f45a03b56008cb0c8244cb63616` |

## Rebuilding / upgrading

`build-deepfilternet.ps1` is the single source of truth for how the binaries are
produced (pinned tag + commit + Rust toolchain). To rebuild, dispatch the
**Build deepfilter.dll** workflow from the Actions tab and commit the artifact —
no local Rust toolchain needed. To upgrade upstream, bump the pins in the script
(see its header comment), then do the same and update this table.

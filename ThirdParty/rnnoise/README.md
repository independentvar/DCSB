# Vendored rnnoise (RNN-based noise suppression)

`rnnoise.dll` (x64) is built from [xiph/rnnoise](https://github.com/xiph/rnnoise),
BSD-3-Clause (see `COPYING`). It powers the "Suppress background noise" option on
the microphone's Fast mode (`DCSB.SoundPlayer.RNNoiseSuppressor` P/Invokes it) and is copied to
the app output by `DCSB.SoundPlayer.csproj`, from where the installer generator
ships it like any other dll.

## Provenance of the committed dll

| | |
|---|---|
| Upstream tag | `v0.2` |
| Upstream commit | `904a876dce1f9ab8860c0a5000ed151f9f6eef58` |
| Model weights | `rnnoise_data-0b50c45.tar.gz`, SHA-256 `4ac81c5c0884ec4bd5907026aaae16209b7b76cd9d7f71af582094a2f98f4b43` |
| Built by | `build-rnnoise` GitHub Actions workflow (windows-latest, MSVC), run 29122714635 |
| `rnnoise.dll` SHA-256 | `693345d9e326315a2114c2ff76c837da7cfc8633cc4ed6ff790e9712e150c80b` |

## Rebuilding / upgrading

`build-rnnoise.ps1` is the single source of truth for how the dll is produced
(pinned tag + commit, hash-checked model download, MSVC flags). To rebuild,
dispatch the **Build rnnoise.dll** workflow from the Actions tab and commit the
artifact — no local C toolchain needed. To upgrade upstream, bump the pins in
the script (see its header comment), then do the same and update this table.

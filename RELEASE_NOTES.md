# YFM Fusion Companion v1.0.0

First public Windows x64 release of the standalone, English-only Yu-Gi-Oh! Forbidden Memories companion.

## Included

- Manual turn adviser for ordered hand chains and one player-field starting card.
- Exact 40-card deck analysis across all 658,008 physical five-card hands.
- Owned-card optimizer with multiple strategy profiles and ownership/copy limits.
- Read-only SwanStation memory-card import.
- Read-only RetroArch/SwanStation live hand, field, deck, collection, terrain, and Life Point display for the supported NTSC-U game.
- Compact Live sidecar showing only result, effective ATK, and numeric hand/field route notation.
- Illustrated PDF instruction booklet and text-only Markdown manual.

## Fusion accuracy

The bundled database contains 722 cards and 25,146 canonical unordered fusion pairs. The pair mapping was exhaustively compared with the packed fusion table on the supplied original USA disc: zero missing pairs, zero extra pairs, and zero differing result IDs.

Live advice samples the advice-relevant duel state twice. If the hand or field changes between RetroArch's separate memory reads, the companion withholds advice for that refresh and retries automatically instead of presenting a potentially torn hand/slot snapshot.

## Installation

1. Download `yfm_companion_windows_x64_v1.0.0.zip` and its SHA-256 file.
2. Extract the entire ZIP to a normal folder.
3. Run `install_dependencies.cmd` first.
4. Run `YFM Fusion Companion.exe`.

The package is self-contained; ordinary users do not need to install .NET. The application is not code-signed, so Windows SmartScreen may appear.

## Compatibility and safety

- Windows 10 or 11 x64.
- Live mode: NTSC-U / SLUS-01411 through RetroArch with SwanStation.
- Manual analysis remains available without RetroArch.
- The companion never writes game memory, saves, ROMs, RetroArch configuration, or controller input.
- No ROM, BIOS, save file, credentials, or card artwork is included.

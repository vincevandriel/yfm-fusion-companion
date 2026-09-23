# YFM Fusion Companion v1.1.0

This public Windows x64 release completes the campaign-planning work and fixes the startup path that could incorrectly report a database failure.

## Important upgrade

Upgrade from v1.0.0 if the companion displayed **“The card database could not be loaded”** when RetroArch or a save was unavailable. The database is now initialized independently from optional save/live services. A genuine missing database is reported precisely; optional RetroArch or save discovery failures leave the manual adviser, Deck Analyzer, and Owned-card Optimizer ready to use.

## New and improved

- Campaign-aware deck planning for the general campaign, an individual duelist, or the final gauntlet.
- Opponent threat modelling with direct and material-limited chained fusion routes, documented opportunity weighting, terrain-aware safety comparisons, removal answers, opening-hand coverage, and explicit best-found/uncertainty labels.
- Optional virtual Star Chip plans. The planner keeps a feasible no-spend deck unless spending is strictly better, enforces numeric eight-digit passwords and one purchase per name, and lets the player exclude previously redeemed password-card names.
- Read-only saves with an empty or partial constructed deck still import the owned chest. Invalid Star Chip values are withheld rather than treated as spendable.
- Full Live Duel now shows both guardian-star chains and conservative field outcomes. Compact Live keeps its narrow layout: result, ATK, numeric/`F(X)` route, and two small guardian lines only. Unknown enemy star/position remains `F#?` instead of a guess.
- Live Duel refreshes every second without a manual refresh control and retains a non-overlapping read guard.
- Current screenshots, Markdown instructions, dependency installers, release documentation, and an 11-page illustrated PDF guide.

## Included

- Manual ordered-fusion adviser for hand cards and one player-field starting card.
- Exact 40-card analysis across all 658,008 physical five-card opening hands.
- Owned-card optimizer with ownership/copy limits, fields, types, profiles, campaign modes, and virtual Star Chip planning.
- Read-only SwanStation memory-card importer.
- Read-only RetroArch/SwanStation live hand, field, deck, collection, terrain, Life Point, and guardian-star assistance for the supported NTSC-U game.
- Self-contained Windows x64 executable, embedded SQLite card database, research data, dependency check scripts, text documentation, and illustrated PDF manual.

## Verification

- 165 automated regression tests passed with zero failures and zero skips.
- 13 independent Phase 4 acceptance probes passed.
- 14 cross-component integration checks passed; two checks that require a specifically supplied save path or RetroArch configuration were reported as skipped rather than passed.
- The SQLite catalog rebuild is deterministic: 722 cards and 25,146 resolved fusion pairs.
- The packaged executable passed an isolated startup smoke test, including bundled database initialization.

## Installation

1. Download `yfm_companion_windows_x64_v1.1.0.zip` and its SHA-256 file.
2. Extract the entire ZIP to a normal folder. Do not run the executable from inside the ZIP.
3. Run `install_dependencies.cmd` first.
4. Run `YFM Fusion Companion.exe`.

The package is self-contained; ordinary users do not need to install .NET. Windows SmartScreen may appear because the application is not code-signed.

## Compatibility and safety

- Windows 10 or 11 x64.
- Live mode: NTSC-U / SLUS-01411 through RetroArch with SwanStation.
- Manual analysis remains usable without RetroArch, a save, or an active duel.
- The companion never writes game memory, saves, ROMs, RetroArch configuration, or controller input.
- No ROM, BIOS, save file, credentials, or copyrighted card artwork is included.
- Campaign recommendations are modeled best-found guidance, not a guaranteed game-win prediction. Unknown CPU guardian choices and unverified live battle state are never invented.

# Phase 4 checkpoint — repairs verified, local release gate passed

2026-09-23. Original application baseline: `974cce4`. Initial audit-only checkpoint: `7f51857`.

The nine findings from the independent Phase 4 audit have been repaired and re-audited. **The repaired local release gate passes.** This checkpoint supersedes the 2026-09-21 blocked checkpoint; the detailed closure and validation boundaries are in [`docs/audit/PHASE4_FINAL_AUDIT.md`](docs/audit/PHASE4_FINAL_AUDIT.md).

## Verified repaired state

- 165 regression tests passed with zero failures or skips; Release build passed with warnings as errors.
- Formatting, whitespace, and configured analyzers passed at informational severity.
- All 13 independent Phase 4 probes passed; current production-source hashes are stored in `docs/audit/phase4-acceptance-results.json`.
- Strict research validation passed: 39 opponents, 3,681 pool entries, exact campaign groups and guardian cycles, and a 2,048 weight total for every opponent.
- SQLite reproduced byte-for-byte: 722 cards, 25,146 resolved pairs, SHA-256 `9E24D9D5518E1B9FBEE87872121EC0A59D6ECDDEBC64096156179E55A1664E0C`.
- The database now initializes independently from optional save/live services; the audit renderer copies `Data/yfm.db`, and startup-only plus self-contained package smoke tests passed.
- 14 integration checks passed and 2 explicitly environment-dependent checks were skipped because this automated run was not given a live save path or RetroArch configuration.
- Eight desktop workflow renders passed, including a visible nonempty Star Chip purchase plan and both guardian-aware Full/Compact Live layouts.
- The 11-page PDF manual was regenerated and visually checked.
- The canonical package includes the executable, embedded database, research data, dependency installers, user documentation, source archive, audit evidence, and SHA-256 manifests; an independently extracted copy matched byte-for-byte.

## Preserved boundaries

- Compact Live remains result name, ATK, numeric/`F(X)` route, and two small guardian lines only. It does not add material names or widen the sidecar.
- Unknown enemy battle position or active guardian star is displayed as `F#?`; the application does not guess.
- Campaign output is labeled modeled/best-found rather than a guaranteed global optimum or win rate.
- Saves, game memory, ROMs, and RetroArch configuration remain read-only.
- No GitHub tag, public release, installed copy, save, or emulator configuration was changed by this repair.

The next external action would be publishing the verified source/candidate, but that is separate from this completed local repair gate.

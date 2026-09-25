# Phase 2 gate — automatic saves and desktop redesign

Date: 2026-09-24

Historical status at the Phase 2 handoff: **passed**. **Reopened by Phase 3 on 2026-09-25** after independent review found engine-state, source-race, virtualization and visual-acceptance gaps. See [PHASE3_AUDIT.md](PHASE3_AUDIT.md) for the current status; the historical timings below do not establish release readiness.

## Automated verification

- `dotnet test YfmFusionCompanion.sln --no-restore`: 194 passed, 0 failed, 0 skipped.
- `dotnet build YfmFusionCompanion.sln --no-restore -warnaserror`: succeeded with 0 warnings and 0 errors.
- `dotnet format YfmFusionCompanion.sln --no-restore --verify-no-changes`: passed.
- `tools/YfmCompanion.UiAudit` used a generated, non-personal PS1 memory-card fixture and restored the user's desktop settings after the run.
- The UI audit captured all five tabs plus optimizer gallery, progress, result, and high-DPI/adaptive layouts. It recorded 40 forward keyboard-focus visits.

## Measured UI acceptance results

The final synthetic-save run recorded:

| Check | Result | Phase 3 target | Outcome |
|---|---:|---:|---|
| Busy state visible | 75 ms | ≤ 200 ms | Pass |
| First legal 40-card candidate visible | 1,258 ms | within the 5-second Quick budget | Pass |
| Pause response | 592 ms | ≤ 1,000 ms | Pass |
| Stop-and-keep-best response | 551 ms | ≤ 1,000 ms | Pass |
| Captured scenarios | 15 screenshots | required resolutions/scales and all tabs | Pass |

The first legal deck is deliberately labelled **evaluation pending** until sampled statistics exist. An estimated result reports its sample count and uncertainty; it is not presented as exact or proven optimal.

Machine-readable measurements are in [`phase2-ui/ui-audit.json`](phase2-ui/ui-audit.json). Representative evidence includes:

- [`1920x1080-150-owned-optimizer.png`](phase2-ui/1920x1080-150-owned-optimizer.png)
- [`1920x1080-150-owned-gallery.png`](phase2-ui/1920x1080-150-owned-gallery.png)
- [`1920x1080-150-quick-progress.png`](phase2-ui/1920x1080-150-quick-progress.png)
- [`1920x1080-150-quick-result-deck.png`](phase2-ui/1920x1080-150-quick-result-deck.png)
- [`1366x768-2-owned-optimizer.png`](phase2-ui/1366x768-2-owned-optimizer.png)
- [`1920x1080-150-save-snapshot.png`](phase2-ui/1920x1080-150-save-snapshot.png)
- [`1920x1080-150-live-duel.png`](phase2-ui/1920x1080-150-live-duel.png)

## Save and collection coverage

Automated tests cover renamed saves, automatic newest-valid selection, rejection of a newer corrupt candidate with a visible warning, pinned/manual modes, stale retained data, chest ownership with an empty deck, and deck-plus-chest totals. The desktop uses the same collection snapshot for Save Snapshot, current-deck loading, and the optimizer. A running optimizer freezes its source; a newly detected save is held as pending rather than silently changing the job.

The default collection view contains positive owned quantities only and is virtualized. It supports search, type filtering, sorting, keyboard autocomplete, manual quantity edits, optional local artwork, and bounded decoded-thumbnail caching. Personal saves and artwork are not included in the evidence or repository.

## Design and integration review

All full-workspace tabs use the shared navy, lapis-blue, white, and restrained-gold system. Normal buttons no longer use white-on-white states. The layouts retain access to controls at 1366×768 and 1920×1080 at 125%, 150%, and 200% scaling; constrained high-DPI cases expose deliberate scrolling instead of clipping controls permanently.

Compact Live remains intentionally narrow and unchanged in information scope: result name, ATK, numeric/`F(X)` route notation, and guardian-star lines only. No material names or gallery controls were added.

Phase 3 must independently challenge these claims, exercise actual live/save environments where available, compare performance before and after, and fix any failure in its responsible phase before release.

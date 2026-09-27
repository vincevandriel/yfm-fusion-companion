# Phase 3 Checkpoint — Campaign Optimizer Desktop Integration

> **Superseded completion assessment (Phase 4, 2026-09-21):** the work below completed an optimizer-UI subset, not the full agreed Phase 3 gate. Live guardian advice, complete workflow validation, updated guides and a tested release candidate remain unfinished. See `docs/audit/PHASE4_FINAL_AUDIT.md` and `PHASE_4_CHECKPOINT.md` before continuing. The prior visual sign-off also missed clipped table content.

**Status:** Complete and verified on 21 September 2026.  Work is intentionally paused before Phase 4.

## What Phase 3 added

- A native, offline campaign-planning area in **Owned-Card Optimizer**.
  - Campaign goal choices: general campaign safety, one specific duelist, final boss gauntlet, and the pre-existing manual profile mode.
  - The duelist chooser contains the 39 bundled opponent profiles and marks whether each is unlocked by the currently loaded save snapshot.
  - A clearly labelled, optional **Use saved Star Chips** setting.  It reads the snapshot budget and produces a virtual purchase plan only; it never writes to the save file or spends Star Chips in-game.
  - Campaign plans preserve the existing exhaustive 40-card hand analysis and add coverage/tie-break context, purchase recommendations, and opponent-threat information.
- Guardian Star information is presented using the game symbols in the campaign threat results.
- The existing dark desktop palette now also applies to combo boxes, including their disabled state.
- Optimizer result tabs were shortened to remain on one line at the supported desktop size.
- Compact Live was not expanded or otherwise changed.  Its route format remains result name, ATK, and numeric / `F(X)` notation only.
- The deterministic UI renderer now exercises the campaign controls, generates a real campaign plan view, and keeps its existing Compact Live boundary check.

## Verification evidence

Final automated verification completed successfully:

- `dotnet test YfmFusionCompanion.sln --no-restore --verbosity quiet`: **158 passed, 0 failed, 0 skipped**.
- `dotnet build src\\YfmCompanion.Desktop\\YfmCompanion.Desktop.csproj -c Release --no-restore -warnaserror`: **0 warnings, 0 errors**.
- Analyzer and whitespace format verification both completed without required changes.
- `git diff --check` completed cleanly.
- The deterministic renderer produced and checked normal, Deck Analyzer, Live Duel, Live Duel Inspector, Save Snapshot, Owned-Card Optimizer, Campaign Plan, and Compact Live views at the intended desktop dimensions.
- Manual visual review confirmed the campaign target hint is readable, the result tabs stay on one row, and Compact Live retains its intentionally compact three-column presentation.

The most recent rendered evidence is in the disposable local diagnostic folder:

`C:\Users\vince\AppData\Local\Temp\YfmPhase3FinalUi-923bcb11866c4c61a6954e02b45c9d6a`

## Scope boundary

No release was published, no installed companion copy was replaced, no save file was modified, and no RetroArch/game state was touched during this phase.

**Do not begin Phase 4 until the user explicitly asks to continue.**

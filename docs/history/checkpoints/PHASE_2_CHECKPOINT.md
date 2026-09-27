# Phase 2 completion checkpoint — 2026-09-20

## Status

Phase 2 is complete and verified. Phase 3 UI integration has not begun. No Phase 2 build has been installed, published, or copied over the existing one-second Live Duel release.

## Completed engine work

- The read-only save snapshot exposes Star Chips and unlocked Free Duel opponents.
- Guardian-star cycles, symbols, and the 500-point battle modifier are centralized and fail closed for unknown values.
- Concrete opponent Deck pools produce ranked base-monster and non-glitch fusion threats without describing weights as win probabilities.
- The four Phase 1 research JSON files are copied into build/publish output and loaded through a validating runtime boundary. Schema, source revision, opponent IDs, Deck pools, policy scopes, and guardian-star consistency are checked before use.
- Campaign targeting supports:
  - the 33-opponent general-safety group;
  - one explicitly selected opponent;
  - the six-opponent final-gauntlet group.
- Opponent counter scoring considers concrete ATK, both printed Guardian Stars, relevant removal types, and field effects. When the opponent's selected star is unproven, scoring conservatively checks every printed option and never infers AI star choice.
- General-safety finalist selection uses final-gauntlet counter coverage only as a near-equal tie-break after preserving general-safety coverage and allowing no more than 0.5 percentage-point differences in exact hand metrics.
- Star-chip mode is virtual and read-only. It respects the available budget, permits each password-shop card name at most once, removes purchases not used by the recomputed 40-card deck, and labels the result as best found rather than globally optimal.
- Exact Deck analysis now evaluates each unique five-card multiset once and applies its exact combinatorial multiplicity. This preserves all 658,008 physical five-card hands while avoiding repeated evaluation of identical hands.

## Verification evidence

- Complete automated suite: **158 passed, 0 failed, 0 skipped**.
- Release solution build: **0 warnings, 0 errors**.
- Formatting/static verification: passed with no changes required.
- `git diff --check`: passed.
- Focused real-catalog campaign tests cover the 33-opponent general group, a named opponent, the six-opponent gauntlet, and campaign optimizer wiring.
- Release output contains all four required `ResearchData` JSON files.

## Deliberate claim boundaries

- Opponent opportunity weights and heuristic counter-coverage scores are not duel-win probabilities.
- AI field-spell and Guardian-Star selection behavior is not assumed from the provisional reimplementation flags.
- Optimization results are deterministic best-found results, not proofs of global optimality.
- No save file or game memory is written by the Phase 2 engine.

## Resume instruction

If work continues, begin Phase 3 from this checkpoint by integrating the verified Phase 2 APIs into the native desktop UI. Re-run the complete suite before installing or publishing any build. Preserve the current compact-live boundary and do not widen Compact Live with card names.

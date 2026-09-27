# Guide-informed support scoring

The 2026-09-27 [fan deck research update](FAN_DECK_RESEARCH.md) supersedes the weights below for the current build. It adds natural-body availability, strict >4,500 setup/clear coverage, support-only draw penalties and six adapted community strategy starts. The remainder of this document records the previous guide-scoring rationale and validation.

Reviewed 2026-09-26 against the original Forbidden Memories rules and current catalog. These are experienced player/speedrunner guides, not an official or professionally certified scoring formula.

## Sources and reassessment

- [GFC_, vanilla speedrunning guide](https://www.speedrun.com/yugiohfm/guides/o9dax): build reliable Dragon/Thunder fusion access; useful compatible equips and Raigeki are essential to that route; roughly three equips are a common progression target. Umi often helps Twin-headed Thunder Dragon without also boosting enemy Dragons. Widespread Ruin provides a single attack-triggered answer, not an entire-board wipe. Strong late opponents and enemy equipment can exceed the player's nominal attack floor.
- [elvencloud, speedrunning guide](https://www.speedrun.com/nl-NL/yugiohfm/guides/x53ec): adapt to available cards; consistent fusions plus two/three equips or a stronger payoff are progression markers. It recommends farming suitable equips/traps, not retaining arbitrary support.
- [RetroAchievements mastery guide](https://github.com/RetroAchievements/guides/wiki/(WIP)Yu%E2%80%90Gi%E2%80%90Oh!-Forbidden-Memories): transition from a Twin-headed fusion deck with equips/terrain to stronger natural monsters and compatible power-ups; replacement terrain can help against hostile starting fields. Its full achievement/farming deck is a specialized goal, not a universal fixed ratio.
- [Exarion, 15-card extension guide](https://www.speedrun.com/yugiohfmextensions/guides/mmi3a): corroborates the fusion/support mix and warns that field versus removal trade-offs depend on objectives. Its modified-drop category was not used as a source for vanilla acquisition rates or hard card ratios.

The guides support the user's one useful field and two compatible equip copies as a reasonable minimum structure target for a fusion route, while often recommending more equips. They do not establish that every late-game deck must include a field: strong natural monsters and opposing terrain can change that decision. The model therefore rewards that target and reports shortages rather than rejecting legal decks or inventing unavailable copies.

## Implemented model

The same hand enumeration and physical-copy multiplicity accounting now additionally count:

- hands containing a potential >=3500 or >=4500 ATK setup;
- hands drawing Raigeki or Dark Hole (board clear);
- hands drawing either clear or Widespread Ruin (broad defensive-removal availability);
- the exact union of >=3500 setup or board clear, without double counting;
- maximum and total best setup attack.

For each natural monster or actual fusion state, only unconsumed cards from the same hand can support the final monster. Each compatible equip copy adds its validated +500 or Megamorph +1000 once. At most one beneficial terrain modifier is added; duplicate fields never stack. Equip cards consumed as fusion materials cannot also power up the monster. Nothing is assumed already on the board. These are potential setups that may require separate turns and the monster to survive, not single-turn plays or win probabilities. Enemy responses, Reverse Trap, actual starting terrain and enemy equipment are not simulated in these setup counts. Guardian-based threat scoring remains a separate tie-break.

Legacy immediate fusion metrics and one-terminal-equip routes retain their existing meanings. Support setup statistics are separately named and presented. A hand's fusion probability is not renamed to a setup probability.

Balanced, ControlAndSafety and FieldAndType final comparisons now begin with fixed guide-informed support points: 40 times setup-or-clear availability, 25 times >=2800 immediate fusion availability, 15 times broad-removal availability, 10 times >=3500 setup availability, five times >=4500 setup availability and five times clear availability. Counts are compared as rational numbers across sample sizes using integer arithmetic. This is a heuristic policy chosen for the application; the guides do not provide these numeric weights. It is not a fitted win model.

A separate structure bonus contributes up to ten points: three for one useful field, three for each of the first two compatible equip copies and one for a third. All must support the same established >=2000-ATK target (a natural monster or fusion outcome appearing in at least 5% of the analyzed hands). Further copies must earn their place through draw/setup probability; no structure bonus is given just for filling spell slots. The strongest scoring package and any shortfall are shown in the result. This guards against the initial availability-only score dropping every equip and field in favour of removal.

After guide support, the existing per-opponent coverage, gauntlet tie-breaks, exact fusion metrics, purchase costs and deterministic numeric card IDs break ties. Explicit FusionConsistency, MaximumPower and RitualExperiment profiles retain their specialized primary power scoring. Seed construction still penalizes fields that boost selected enemy types, but setup attack counts do not themselves subtract an enemy field bonus. Consequently equal nominal attack is not a proven answer against every opponent.

Dark Hole now receives a positive recovery counter value, below Raigeki because it destroys the player's board too. Widespread remains a defensive attack trap, never classified as a board clear. Crush Card's attack threshold is checked instead of treating it as universally effective. A preferred field is no longer automatically supplied as already-active terrain to campaign safety scoring.

Both rules and objective identities changed (`concrete-fusion-hand-support-v2`, `campaign-guide-support-v4`). Old proof checkpoints are rejected as incompatible and preserved by the existing restart/backup workflow. Four search/eight exact-analysis Auto workers, bounded caches, cancellation and deterministic merges remain in place.

## Validation and limits

New cases cover stacked physical equips, non-stacking duplicate terrain, incompatible equips, consumed-material exclusion, setup/clear union accounting, coherent support structures, score transitivity across sample sizes, Dark Hole versus Raigeki and conditional Crush Card. The full cache/parallel cancellation/progress and proof-recovery suites are included. The real-catalog benchmark uses synthetic inventory, not the user's current save. Its final deck is best found, not proven optimal or a guaranteed campaign win. See `../benchmarks/guide-scoring.json` for exact 658,008-hand reports, selected copies, before/after comparison and timings.

No game/save/ROM/config writes or farming gameplay was performed. Source before this change is retained in `artifacts/guide-scoring-before/`. Gameplay responsiveness was not retested.

Current validation result: 218 tests passed, none failed/skipped. Production WPF presentation asserts the support statistics survive installing the final result. The real-catalog Quick run examined 80,772 candidates, then exactly verified 658,008 hands. It retained three Umi, six compatible equip copies, Raigeki, Dark Hole and two Widespread Ruin. The coherent package targets Twin-headed Thunder Dragon; maximum available same-hand setup was 4,300 ATK, >=3500 setup availability 10.13%, board-clear availability 23.72%. This synthetic fixture is not the user's deck. A fixed one-Raigeki substitution that the previous comparator ranked below its material-only baseline is ranked above it by the new guide policy. Current engine-only verification took 46 ms on this duplicate-heavy fixture; no general performance claim is inferred.

# Phase 4 independent final audit

Date: 2026-09-21. Audited application commit: `974cce4`.

**Release gate: BLOCKED. Audit review completed; Phase 4 approval has not been earned.**

This audit follows the agreed four-phase plan: research; mechanics/optimization; integration/release candidate; independent final audit. It is not the older application's original numbered development schedule. The agreed Phase 3 also required live guardian advice, updated instructions, and a freshly extracted release candidate. Its completion checkpoint covered a smaller optimizer-UI scope and was therefore insufficient to close the agreed gate.

No production code was changed during this audit. The added executable acceptance probes deliberately return a failure exit code while the defects remain. They are separate from the existing solution test suite so the historical 158-test baseline remains distinguishable from the new acceptance requirements.

## Evidence and verification

| Check | Result | Boundary |
|---|---|---|
| Release regression suite | 158 passed, 0 failed, 0 skipped; approximately 19 seconds | Some environment-dependent tests return early when fixtures are unconfigured; this is not a fresh live-game validation. |
| Release solution build | 0 warnings, 0 errors | Compilation, not functional acceptance. |
| Solution format verification | Passed | Does not prove algorithmic correctness or eliminate all unused code. |
| New independent acceptance probes | **10 failed, 3 passed** | Synthetic, isolated inputs; details and production-source hashes in `phase4-acceptance-results.json`. |
| Phase 1 data verifier | Passed: 39 opponents, 3,681 Deck entries, five provenance records | Mutation probes show its runtime validation boundary remains incomplete. |
| Pinned source provenance | Source revision and all three input hashes match the manifest | The correctness of unverified AI behavior is not inferred from these hashes. |
| Deterministic research rebuild | All five JSON files byte-identical at recorded generation timestamp | Includes policy, guardian reference, opponent reference, manifest and generation summary. |
| Deterministic SQLite rebuild | Byte-identical; 722 cards, 25,146 resolved pairs | SHA-256: `9e24d9d5518e1b9fbee87872121ec0a59d6ecddebc64096156179e55a1664e0c`. |
| NuGet vulnerability scan | No vulnerable packages reported for solution projects, including transitive packages | Point-in-time NuGet advisory result, not a blanket security certification. |
| UI renderer | All eight screens generated; visual acceptance **failed** | Clipped table content and missing planned guardian presentation. |
| Independent exact-hand control | Passed: 49 successful hands out of 56 physical hands = 0.875 | Confirms duplicate-hand weighting for an independently enumerated fixture. |
| Read-only parse control | Passed: source SHA-256 unchanged | Synthetic in-memory save input, not a comprehensive end-to-end immutability proof. |
| Exact-analysis cancellation control | Passed | A pre-cancelled request throws; cancellation latency during large searches remains unbenchmarked. |
| Current release archive/startup/download validation | Not performed | No complete campaign release candidate was handed off; known correctness blockers prevent approval. |

Local generated evidence: `tmp/phase4-test-results/phase4-baseline.trx`, `tmp/phase4-ui/`, `tmp/phase4-research-rebuild/`, and `tmp/phase4-rebuilt.db`. These are disposable diagnostics, not public release assets. The committed JSON acceptance results contain no user save data or local personal paths.

## Findings

### F01 — High: the campaign selection objective is not the agreed maximum-safety objective

`OwnedDeckOptimizer.cs:38`, `:240`, `:508`, `:517`, and `:540`.

Candidate screening uses the existing fusion-power score and takes the highest-ranked finalists before campaign safety is evaluated. That score gives priority to 2,800 and 2,500 ATK fusion probabilities. A candidate discarded here cannot win on campaign safety later. Final selection uses one aggregate average of standalone counters and summed fusion outcome values; it does not count safe opponents, protect the worst matchup, or calculate early-turn answer coverage.

The final safety assessment also ignores removal and stall cards. Probe **P4-10** holds a 40-card collection fixed, then replaces one weak monster with Raigeki against a threat none of its monsters can answer. The safety score remains **0 with or without Raigeki**. Although removal receives a heuristic bonus during candidate construction, that bonus is absent from the final safety comparison.

Preferred terrain influences some candidate heuristics, but concrete safety comparisons use printed opponent ATK and player ATK plus a guardian adjustment. The final comparison has no representation for opponent terrain, battle position or a terrain-adjusted enemy value. General-mode gauntlet scores also enter candidate construction at a 0.15 multiplier, before the promised near-equal tie-break. The model cannot enforce “lose no safe general matchup” because it has no per-opponent safe-matchup result.

**Return to Phase 2:** implement one explicit objective shared by screening, finalist comparison, purchases and the report. Include per-opponent answers, removal, terrain on both sides, and the agreed gauntlet constraint. Keep uncertainty labels. Add fixtures where a lower raw-ATK deck wins on actual answer coverage and where a gauntlet improvement must be rejected for losing a normal matchup.

### F02 — High: purchase planning can waste chips and incorrectly reject feasible collections

`StarChipDeckPlanner.cs:63`, `:65`, `:95`, `:111`, `:208`.

Enabled purchasing greedily fills a virtual collection before optimizing. It does not evaluate a zero-spend incumbent or alternate purchase bundles. Removing unused purchases afterwards does not establish that retained purchases improve the objective.

- **P4-01:** all available monsters are strategically identical and have no fusion/equip relationships. The owned collection already supplies 40 cards. A lower-ID substitute costing one chip is selected and purchased despite offering no benefit.
- **P4-02:** 38 owned copies plus a 100-chip budget. Buying two different 50-chip cards yields a verified legal 40-card deck. The planner instead consumes the budget on one stronger 100-chip card, then throws an error claiming the collection plus affordable purchases cannot supply 40 cards.

The purchase heuristic uses ATK, DEF, pair connections and equipment. It does not use campaign safety or removal value, so valuable standalone removal can be excluded before deck optimization even considers it.

**Return to Phase 2:** retain the zero-spend deck whenever feasible, establish a feasible purchase completion before optional upgrades, compare complete resulting decks under the same objective, and prefer lower spend for equivalent results. A failed greedy search must not be presented as proof that no affordable legal deck exists.

### F03 — High: opponent threats cover direct pairs only

`OpponentThreatEvaluator.cs:70`, `:105`; `CampaignDeckOptimizer.cs:62`.

The evaluator combines two original pool cards, never a fusion result with another available material. Probe **P4-03** supplies `1+2=4` and `4+3=5`, with cards 1, 2 and 3 in the opponent pool. Only result 4 appears; the much stronger reachable result 5 is missing.

The campaign context defaults to five fusion results plus the single highest-ATK base monster per opponent. Results are sorted by ATK before opportunity weight. Opening hand sizes and recorded deck-generation rules are loaded but not used in the threat calculation. Equipment, field-adjusted threats, opponent control cards, and early-turn draw modeling are absent. Multiplying pool weights is labelled as an opportunity heuristic, which is appropriately cautious, but does not supply the missing requested threat coverage.

**Return to Phase 2:** add material-limited chains, explicit support/terrain interactions and reproducible draw/threat analysis. Distinguish likely threats from theoretical ceilings. Keep CPU guardian choice uncertain until supported by independent evidence; do not replace that uncertainty with an assumption to satisfy an old plan sentence.

### F04 — High: save import loses empty-deck collections and accepts invalid auxiliary values

`Ps1MemoryCardReader.cs:110`; `SaveModels.cs:20` and `:72`.

- **P4-09:** structurally valid matching save copies with an empty deck and 42 chest cards yield zero snapshots. The parser requires every deck slot to be a valid card ID before it exposes the chest. This prevents the clean-rebuild workflow previously requested by the user.
- **P4-08:** the auxiliary chip bytes set to `0xFFFFFFFF` become a spendable balance of **4,294,967,295**, with no warning. The model has no nullable/unavailable state for that field.

The offset tests are synthetic fixtures written at the same offsets the parser reads. They are useful regression checks but do not establish the separately requested displayed-save/executable validation on their own.

**Return to Phase 2:** represent empty/partial deck slots without discarding usable collection data. Validate auxiliary fields against independently established bounds and expose unavailable data plus a warning when necessary. Update the UI and importer for nullable auxiliary values. Keep prior saves out of the fallback path when a newer valid save has an empty deck.

### F05 — High: Full and Compact Live guardian advice is missing

`CompactLivePresentation.cs:6`; `RetroArchNetworkModels.cs:51`; `MainWindow.xaml.cs:1322`.

The shared guardian rule service exists and its basic cycle tests pass. Its two-line chain presentation is not connected to live recommendations. Compact rows still contain only result, attack and route; the full live results have no per-star, per-opponent-slot battle outcomes. The new guardian symbols appear only in the campaign threat list.

The live field record contains slot, card ID, ATK, DEF and power modifier, but no verified battle-position/visibility/selected-star state. The agreed Phase 3 requirement was two compact guardian chains and matchup outcomes without widening the 320-pixel view or adding material names. Preserving the old three-column format alone did not satisfy that addition.

**Return to Phases 2 then 3:** prove required live-state mappings or preserve unknowns as `?`; implement and share the comparison logic; connect both star choices to full and compact presentation. Test attack, defense, tie, face-down/unknown and all occupied opponent slots. Preserve numeric hand routes and `F(X)` notation.

### F06 — Medium: research validation accepts contradictory rule data

`CampaignResearchData.cs:87`, `:108`, `:186`.

- **P4-04:** reversing a guardian cycle is accepted because validation checks names/symbols, not the directional cycle relationships.
- **P4-05:** swapping opponent IDs 1 and 33 between general and final scopes, with matching scope labels, is accepted. Only counts/disjointness/coverage are enforced, not the exact agreed sets.
- **P4-06:** increasing an opponent pool weight by 100 is accepted. Current checked-in pools all total 2,048, but the boundary never enforces that invariant.

The current checked-in files are reproducible and unmodified; these probes demonstrate inadequate rejection of corrupted or contradictory future inputs, not that the shipped data currently has those mutations.

**Return to Phase 2:** validate exact directional edges, all ten stars exactly once, exact campaign groups, and required pool totals. Make both the generator verifier and runtime loader reject the same malformed cases.

### F07 — Medium: password eligibility and prior redemption are incomplete

`StarChipDeckPlanner.cs:63`, `:203`; `MainWindow.xaml.cs:1277`.

Within one plan the name-based set correctly prevents purchasing the same name twice. However, an eight-character string is accepted as a password without checking that it is numeric: **P4-07** recommends `abcdefgh`. There is no prior-redemption input or warning that the save does not establish password-shop purchase history. A recommendation may therefore be unactionable for a card the user already redeemed.

**Return to Phases 2/3:** validate supported numeric passwords against the catalog's purchase eligibility; add an explicit already-redeemed exclusion/manual fallback and explain the uncertainty. Retain the once-per-name constraint and show item cost, password, cumulative spend and remaining budget.

### F08 — High: the release candidate and instruction handoff are incomplete

`README.md:111`, `:154`; `tools/CreateUserGuide.py:346`; `.github/workflows/build-release.yml:39`; `PHASE_3_CHECKPOINT.md`.

The Markdown guide still describes the older profile-only optimizer and says the final suite contains 126 tests. The PDF generator describes that same old workflow and has no campaign/purchase instructions. CI copies the pre-generated PDF; it does not regenerate or verify it against these new features. No freshly extracted campaign release candidate with startup/hash evidence accompanies the Phase 3 checkpoint. Its statement that Phase 3 is complete must be limited to the implemented UI subset.

**Return to Phase 3 after mechanics fixes:** update both guides and labelled screenshots, prepare a fresh self-contained package with the four research JSON files, inspect licenses and dependency requirements, start a freshly extracted copy, compare archive files/hashes and provide the evidence package for re-audit. Publication remains after approval.

### F09 — Medium: UI renderer success does not establish readable or connected workflows

`tools/YfmCompanion.UiRender/Program.cs:348`; `MainWindow.xaml:470` and `:513`.

The fresh `campaign-plan.png` at 1420 by 900 shows card-name and explanation columns compressed to a few visible characters. This is also visible in the earlier Phase 3 image that was called visually clean. The underlying runtime layout may require a shown-window/layout-settling check; the current image is inadequate acceptance evidence regardless of whether the fault is the renderer or application layout.

The renderer calls the optimizer with synthetic inventory directly, then invokes private presentation methods. The visible inventory remains all zero while the result contains a 40-card deck. This does not prove the actual save-to-inventory-to-button-to-result workflow. It also uses no-chip mode and only checks that the purchase grid has a non-null source, not that a real purchase plan is visible and readable.

**Return to Phase 3:** validate the real interaction path with injected deterministic save/live providers, settle layout before rendering, assert useful column widths/readable content, and cover nonempty purchases and threats. Include glyph fallback, supported scaling and unknown live matchup cases before signing off the UI gate.

## Architecture and efficiency observations

The runtime catalog already has one shared SQLite-loading boundary in `FusionCatalog.Load`; the separate importer writes the build-time artifact and is not a redundant runtime connector. Guardian relationships have a shared engine service. The one-second live timer retains an in-flight guard to avoid overlapping reads. These choices should be retained.

The inner optimizer search repeatedly calls `IndividualScore`, `PairScore`, strategy assessment and guardian counter assessment for the same cards and unchanged options. These routines allocate sets/arrays and traverse the entire threat list. Precompute immutable per-request card/pair assessments and use bounded caches. Add representative large-inventory timing/allocation benchmarks and cancellation-latency checks before claiming efficiency; the existing small-fixture tests and renderer with two sampled hands are not those benchmarks.

The established tactical suite passes cases for canonical pair ordering, field-first fusion chains, terminal equipment and the Monsturtle/Spellcaster result of 30,000-Year White Turtle. This audit found no new evidence reversing those existing fusion rules. It did not redo the original executable disassembly or prove every gameplay interaction.

## Repair order and re-audit gate

1. **Phase 2 mechanics:** fix campaign objective/control/terrain evaluation and opponent chain coverage (F01/F03), then purchase selection and eligibility (F02/F07), save handling (F04), strict data rejection (F06), and live comparison prerequisites (F05).
2. **Phase 3 integration:** wire both live star choices and unknown outcomes, finish the user workflow and readable UI evidence (F05/F09), then update guides and package a fresh candidate (F08).
3. **Phase 4 re-audit:** rerun the acceptance probes, add independent cases for the repaired objective and interaction flows, rerun the full regression suite, and review the complete release evidence. All high-priority findings and acceptance failures must close before release approval. Resolve medium validation/UI findings before claiming the agreed final gate is met.

Audit execution completed. Fix implementation and public publication have not been performed by this review. The next work should begin from `PHASE_4_CHECKPOINT.md`.

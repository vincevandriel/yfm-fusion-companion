# Phase 4 final repair audit

Date: 2026-09-23. Original audited application baseline: `974cce4`. Initial audit-only checkpoint: `7f51857`.

**Release gate: PASS for the repaired local candidate. Public publication was not performed by this repair.**

This is the fail-closed re-audit of the nine findings in the 2026-09-21 independent review. It replaces that review's blocked status only for the production source and package verified here. The acceptance JSON deliberately retains `974cce4` as its historical baseline and records fresh hashes for every current production source file.

## Machine-checkable evidence

| Check | Verified result | Boundary |
|---|---|---|
| Release regression suite | 165 passed, 0 failed, 0 skipped; binary coverage evidence captured | Automated coverage is substantial but not a proof of every game state. |
| Release build | Passed with warnings promoted to errors | Compilation is necessary, not sufficient on its own. |
| Formatting and analyzers | Passed at informational severity; whitespace check passed | This verifies configured analyzers and formatting rules. |
| Independent Phase 4 probes | **13 passed, 0 failed** | Synthetic targeted regressions for the original audit findings. |
| Strict research verifier | 39 opponents, 3,681 pool entries, exact campaign groups, exact guardian cycles, and 2,048 weight per opponent | Pinned research data is reproduced and rejected when its enforced invariants are mutated. |
| Deterministic SQLite rebuild | 722 cards and 25,146 resolved pairs; byte-identical SHA-256 `9E24D9D5518E1B9FBEE87872121EC0A59D6ECDDEBC64096156179E55A1664E0C` | The runtime database is read-only and derived from the supplied SQL. |
| Dependency vulnerability scan | No known vulnerable direct or transitive NuGet packages reported | Point-in-time NuGet advisory result, not a permanent security certification. |
| Feature/integration audit | 14 passed, 2 environment-dependent checks skipped | The skipped checks require an explicitly supplied live save path or RetroArch configuration; no path was guessed. |
| Desktop rendering | Five workspaces, closed/open Live inspector states, populated campaign purchase plan, and Compact Live rendered successfully | Deterministic rendered workflow evidence, not a claim about every display scale. |
| Self-contained publish | Exact published executable survived an isolated four-second startup smoke test | Confirms startup/database initialization for the packaged application. |
| Package preflight | Run, Source, and Audit folders assembled; SHA-256 manifest produced; independently extracted archive matched byte-for-byte | No ROM, BIOS, save, RetroArch configuration, or user data is included. |
| PDF manual | 11 pages regenerated and visually inspected page by page | The Markdown README remains text-only by design. |

The exact commands are documented in the repository README and the independent-probe README. The canonical package contains the executable, `Data/yfm.db`, four validated research JSON files, dependency installers, Markdown documents, and the illustrated PDF guide.

## Finding closure

### F01 — campaign objective and safety: closed

Campaign finalists are now compared by per-opponent modeled answer coverage, number of safe matchups, weakest matchup, and opening-hand answer coverage before general power tie-breaks. Guaranteed removal such as Raigeki participates in concrete safety scoring. A selected field modifies both the player's candidate and the opposing threat according to their types. Final-gauntlet improvement is a tie-break after normal-campaign safety rather than a reason to sacrifice a safer general matchup.

The optimizer remains a deterministic **best-found** search over screened candidates, not a mathematical proof of the globally optimal 40-card deck. The UI and reports retain that uncertainty instead of presenting a modeled score as a guaranteed win rate.

### F02 — Star Chip feasibility and spending: closed

The purchase planner first establishes a legal affordable 40-card completion, evaluates the owned-only incumbent whenever it is feasible, and keeps the zero-spend result unless a purchase plan is strictly better under the shared deck objective. Equivalent results prefer lower spending. Purchase cleanup can no longer manufacture a false “no legal deck” conclusion after a greedy optional upgrade consumed the budget.

### F03 — opponent fusion threats: closed within the documented model

Threat generation now includes material-limited sequential fusion chains using two through five pool materials, rather than only direct pairs. Opportunity weights are propagated with saturating arithmetic and labeled as heuristics. The model does not infer undocumented CPU guardian-star choices, hidden battle position, or an exact win probability; those remain explicit limitations.

### F04 — empty decks and invalid auxiliary save values: closed

A structurally valid save with an empty or partial constructed deck now preserves its chest/owned collection. Such a snapshot cannot be loaded as a complete 40-card deck. Star Chips are nullable: values outside the validated `0–999,999` range are withheld with a visible warning rather than becoming spendable currency. Newer valid empty-deck saves are not silently replaced by older deck contents.

### F05 — guardian advice in Full and Compact Live: closed with unknown-state preservation

Both guardian choices now use one shared presentation service. Full Live displays `STAR 1`, `STAR 2`, and `VS FIELD`; Compact Live retains only RESULT, ATK, and numeric/`F(X)` route columns while placing the two small guardian lines under the route. Player ATK is compared with opponent DEF when defense is verified. When opponent position or active guardian star is not proven by the read-only mapping, the companion displays `F#?` and does not guess.

### F06 — strict research validation: closed

Both runtime loading and the research verifier enforce the exact two directional guardian cycles, all ten stars exactly once, the exact general-campaign/final-gauntlet opponent sets, 39 total opponents, 3,681 pool entries, and a total pool weight of 2,048 for each opponent. Mutation probes for reversed cycles, swapped opponent groups, and invalid totals now fail as required.

### F07 — password eligibility and prior redemption: closed

Only eight-digit numeric passwords are eligible for recommendations. Each name can be purchased at most once per plan. The optimizer accepts a manual list of already redeemed password-card names and excludes those names, while clearly stating that the save itself cannot establish prior redemption history. The plan shows password, quantity, individual cost, total, cumulative spending, remaining chips, and reason.

### F08 — release candidate and user instructions: closed locally

The README and illustrated PDF explain the campaign modes, virtual Star Chip planning, empty/partial saves, one-second live updates, guardian notation, and Compact Live constraints. CI regenerates the PDF and runs the strict research verifier plus independent Phase 4 probes before packaging. The local fail-closed audit produced and independently extracted a self-contained candidate. No GitHub tag or public release was created during this repair.

### F09 — readable and connected UI evidence: closed

The campaign grid columns are readable at the audited 1420×900 render size. The deterministic UI audit drives a populated 39-card collection with 70 Star Chips and verifies a nonempty legal purchase plan, rather than attaching a result to an empty visible inventory. It also covers both Live inspector states, unknown guardian outcomes, the full blue/white palette, and Compact Live without widening it or adding material names.

## Database-load incident

The UI audit executable previously omitted `Data/yfm.db`, and the desktop startup catch block labeled any optional save/live initialization exception as a database failure. The render project now copies the canonical database beside its executable. Desktop startup initializes the offline catalog independently through one shared path; a real catalog failure is reported precisely, while an unavailable save or RetroArch connection leaves every offline feature open and schedules a retry. The startup-only renderer and the packaged self-contained smoke test both loaded the database successfully.

## Architecture and efficiency review

- Runtime SQLite access remains confined to the shared `FusionCatalog` data boundary; there are not three competing runtime database connectors.
- The database builder is deliberately separate because it converts and validates the PostgreSQL source at build time rather than serving runtime queries.
- Guardian relationships and display notation share engine services rather than duplicating the cycles in the UI.
- The one-second live timer retains an in-flight guard so slow reads cannot overlap.
- The application remains native Windows/WPF, offline, English-only, and read-only with respect to saves, ROMs, RetroArch configuration, and game memory.

## Remaining validation boundaries

The automated package audit did not perform a fresh live duel because no explicit save path or RetroArch configuration was supplied to that run; the two corresponding integration checks were skipped and reported, not silently passed. Existing controlled NTSC-U evidence remains documented separately. Opponent control behavior, exact draw sequencing, battle-position choices, and active CPU guardian selection are not invented where the verified data does not establish them.

Within those declared boundaries, the original nine audit findings are closed and the repaired local Phase 4 release gate passes.

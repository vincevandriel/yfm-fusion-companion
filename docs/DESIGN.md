# YFM Fusion Companion: design and implementation guide

This is the contributor's map of the 2.0 application. It explains the actual implementation, its boundaries, the reasons behind important choices, and where to make changes. The generated [source index](reference/SOURCE_INDEX.md) lists every production C#/XAML file with a navigational index of declarations and named controls with direct source links. Use this document to understand a subsystem, then use that index to reach its code.

The portable release includes a searchable, offline HTML edition. The source repository is authoritative; historical decisions and test evidence live under [history](history/README.md), [audit](audit/RELEASE_2_0_1.md), [research](research/FAN_DECK_RESEARCH.md) and [benchmarks](benchmarks/CAMPAIGN_DECK_RESULTS.md).

<!-- TOC -->

- [1. Product and trust boundaries](#1-product-and-trust-boundaries)
- [2. Architecture and dependency direction](#2-architecture-and-dependency-direction)
- [3. Repository and release layout](#3-repository-and-release-layout)
- [4. Startup and shutdown](#4-startup-and-shutdown)
- [5. Catalog models and SQLite schema](#5-catalog-models-and-sqlite-schema)
- [6. Database import and reproducibility](#6-database-import-and-reproducibility)
- [7. Card search and input controls](#7-card-search-and-input-controls)
- [8. Tactical fusion planning](#8-tactical-fusion-planning)
- [9. Exact opening-hand analysis](#9-exact-opening-hand-analysis)
- [10. Sampled analysis and uncertainty](#10-sampled-analysis-and-uncertainty)
- [11. Support, terrain, equips and removal](#11-support-terrain-equips-and-removal)
- [12. Parallelism, caches and determinism](#12-parallelism-caches-and-determinism)
- [13. Legal deck and virtual purchase space](#13-legal-deck-and-virtual-purchase-space)
- [14. Candidate construction and search seeds](#14-candidate-construction-and-search-seeds)
- [15. Frozen jobs and lifecycle state machine](#15-frozen-jobs-and-lifecycle-state-machine)
- [16. Objective ordering and score versions](#16-objective-ordering-and-score-versions)
- [17. Durable proof and checkpoint recovery](#17-durable-proof-and-checkpoint-recovery)
- [18. Campaign research and opponent modeling](#18-campaign-research-and-opponent-modeling)
- [19. Recommended deck library](#19-recommended-deck-library)
- [20. Save parsing and collection ownership](#20-save-parsing-and-collection-ownership)
- [21. Read-only UDP transport](#21-read-only-udp-transport)
- [22. Live decoding and transient state](#22-live-decoding-and-transient-state)
- [23. Guardian stars and compact presentation](#23-guardian-stars-and-compact-presentation)
- [24. WPF views, control ownership and layout](#24-wpf-views-control-ownership-and-layout)
- [25. Artwork, icon and memory use](#25-artwork-icon-and-memory-use)
- [26. Preferences, progress and diagnostics](#26-preferences-progress-and-diagnostics)
- [27. Testing and audit layers](#27-testing-and-audit-layers)
- [28. Reproducible release process](#28-reproducible-release-process)
- [29. How to change the application safely](#29-how-to-change-the-application-safely)
- [30. Troubleshooting by symptom](#30-troubleshooting-by-symptom)
- [31. Known limits and extension boundaries](#31-known-limits-and-extension-boundaries)
- [32. Complete source navigation](#32-complete-source-navigation)

<!-- /TOC -->

## 1. Product and trust boundaries

The companion helps a player plan legal Forbidden Memories fusions, inspect live duel state, analyze a selected deck and search their owned collection for a useful 40-card deck. It is a Windows WPF program. The working catalog, artwork and research are local; ordinary use does not require an internet service or an AI model.

The optimizer is a deterministic search and rules engine. Its CPU workers evaluate candidate decks and opening hands. There are no neural-network weights, cloud inference, automatic gameplay or GPU inference calls. More workers help only when independent work is large enough to exceed scheduling/cache overhead.

Five boundaries must survive a contribution:

1. Emulator access exposes only status and memory reads. It never writes RAM, sends controller input, alters configuration or creates save states.
2. Save files are opened for reading. A collection is accepted only after structural/content validation. A remembered older snapshot is explicitly stale, not a substitute for a current valid empty deck.
3. Exact analysis means exact enumeration for the selected deck under modeled rules. It does not mean a measured duel win rate or globally optimal deck.
4. Timed search returns the best found. Only exhausted proof enumeration can establish optimum within its frozen legal space and objective.
5. Unknown live information stays unknown. A failed or inconsistent read must not turn an old hand position into fresh actionable advice.

## 2. Architecture and dependency direction

```text
Desktop (WPF windows, controls, async orchestration)
  +-- Engine (fusion search, analysis, deck construction, proof, campaign scoring)
  |     +-- Data (immutable catalog, SQL import, SQLite validation)
  +-- RetroArch (UDP reads, live decoding, save parsing/discovery)
  +-- Data

Tests exercise Data + Engine + RetroArch and selected portable desktop helpers.
UiRender and UiAudit exercise the actual WPF assembly.
Publish-Release invokes all required checks and packages the Windows executable.
```

| Project | Responsibility | Deliberately outside its responsibility |
|---|---|---|
| `YfmCompanion.Data` | Catalog models, deterministic import, normalized SQLite, resource-root resolution | UI state, player ownership, network requests |
| `YfmCompanion.Engine` | Rules, hand metrics, campaign threat models, legal deck search and proof | Reading personal saves or choosing a desktop file |
| `YfmCompanion.RetroArch` | Read-only transport, validated live/save structures, source selection | Scoring a deck or changing the game |
| `YfmCompanion.Desktop` | Presentation, settings, worker lifecycle, source installation, user actions | Direct SQLite queries or unchecked memory interpretation |
| `YfmCompanion.Tests` | Fast fixtures and real-catalog regression tests | Claiming visible UI or real gameplay acceptance |

These dependencies are enforced by project references. SQLite connections belong in Data. Do not duplicate card catalogs in the UI or introduce emulator commands into Engine.

## 3. Repository and release layout

The repository separates `src/`, `tests/`, `tools/`, `assets/`, `database-source/` and `docs/`. `artifacts/yfm.db` is the canonical, versioned runtime database. Other `artifacts/` contents and `bin/obj` are generated and ignored. Historical checkpoints are small text records under `docs/history/checkpoints/`; Git retains the implementation history.

The player package is intentionally different from the source tree:

```text
YFM-Fusion-Companion-2.0.1/
  YFM Fusion Companion.exe
  START-HERE.txt
  CHECKSUMS.sha256
  Resources/
    Data/yfm.db
    Artwork/                 # 722 images and their attribution/manifests
    ResearchData/            # campaign policy, opponents, stars, provenance
  Documentation/             # user guide, design guide, source index, audit
  Licenses/
```

[`RuntimeResources.FindRoot`](../src/YfmCompanion.Data/RuntimeResources.cs) selects `Resources` when that directory exists. Development and older flat layouts fall back to the executable directory. Selection is made for the whole resource root: a partially extracted new package cannot silently borrow a stale database or artwork from an old sibling directory.

The executable is self-contained, Windows x64, single-file and includes the .NET runtime/native dependencies. Content data remains external for inspectability and easy integrity checks. A release ZIP is not a source distribution; GitHub's source view/archive contains the editable implementation.

## 4. Startup and shutdown

[`App`](../src/YfmCompanion.Desktop/App.xaml.cs) normally starts `MainWindow`. `MainWindow` initializes XAML, connects view aliases, restores preferences, and calls `InitializeOfflineWorkspace`. That method loads the catalog, creates the tactical planner and analyzer, builds card pickers and owned-card rows, and initializes campaign controls. Optional save/live services are separate from offline database initialization so a missing emulator does not disable manual tools.

`--verify-package <report.json>` is an explicit release self-check. It bypasses the ordinary window, personal preferences, save discovery and emulator connection. `PackageVerifier` opens the packaged database, validates research, loads six reference builds and hashes/decodes all 722 artwork images. It writes a JSON result and exits with a success/failure code. This checks the actual published executable, not a development DLL that happens to remain alive.

The window owns a cancellation source and `WindowActivityTracker`. On close it disables interaction, stops timers/watchers, cancels preparation/analysis, requests a safe optimizer pause, saves preferences, and waits for tracked activities and checkpoint writes. It then schedules the final close on the dispatcher to avoid reentrant closing events. The closed handler disposes watchers, transport and cancellation resources.

Do not replace this drain with an unconditional process exit: proof checkpoints and late callbacks must finish or be cancelled before UI objects disappear.

## 5. Catalog models and SQLite schema

[`Models.cs`](../src/YfmCompanion.Data/Models.cs) defines `Card`, `FusionPair`, `FusionResolution`, `FusionRuleReference`, `EquipResolution`, `CardAdvancedDetails` and import reports. IDs are the game card numbers, 1 through 722. Card names are display data; identity uses IDs. The catalog has 25,146 resolved fusion pairs.

[`SqliteSchema`](../src/YfmCompanion.Data/SqliteSchema.cs) describes tables for cards, categories and membership, source/provenance records, ordered general/exact fusion rules, indexed rule operands, resolved pairs and equips. Foreign keys and checks reject orphan card IDs, invalid flags and malformed relations. Card attributes, types, guardians, costs and provenance remain queryable rather than being baked into UI strings.

[`FusionCatalog`](../src/YfmCompanion.Data/FusionCatalog.cs) loads and validates the SQLite data through a read-only connection, then exposes in-memory lookup structures and resolutions. Consumers share the catalog because lookup data is immutable. Per-search scratch state belongs in worker instances, not in the catalog.

`Resolve(first, second, includeGlitches)` normalizes the pair lookup and returns the final card with source-rule references and intended/glitch flags. `ResolveEquip` uses verified compatibility and bonus data. `GetAdvancedDetails` supports the inspector without making the desktop query SQLite.

## 6. Database import and reproducibility

[`PostgresDumpParser`](../src/YfmCompanion.Data/PostgresDumpParser.cs) parses the supported PostgreSQL dump constructs, including escaped COPY data. It does not execute arbitrary dump SQL against a running PostgreSQL server. [`PostgresDumpImporter`](../src/YfmCompanion.Data/PostgresDumpImporter.cs) maps the supported source data into the normalized SQLite schema, builds resolved relationships, runs integrity checks and reports source/output hashes and row counts.

`tools/YfmCompanion.DataBuilder` is the command-line entry point. The release publisher rebuilds the database from `database-source/YuGiOh_Forbidden_Memories_PostgreSQL.sql` and compares its SHA-256 with `artifacts/yfm.db`. A mismatch blocks publication. Update the supplied source and deterministic builder together; never hand-edit a player's runtime database to change a game rule.

Import tests cover escaping, ordering, duplicate/integrity failures and real-catalog golden pairs. Adding a new source format requires an explicit parser path and tests, not loosening current validation until arbitrary input happens to pass.

## 7. Card search and input controls

[`CardSearchService`](../src/YfmCompanion.Engine/CardSearchService.cs) ranks card-number, exact-name, prefix and substring matches with a bounded result count. [`CardPicker`](../src/YfmCompanion.Desktop/Controls/CardPicker.xaml.cs) presents suggestions and keyboard acceptance; it does not implement a second search engine. Empty slots are valid, and two copies of one card remain separate physical slots.

`CardTile` renders artwork/details. `DeckTrayViewModel` groups selected deck cards and supports add/remove operations. `MainWindow.DeckEditor.cs` connects the tray to the underlying forty pickers and validates a load from a saved deck. Selection and quantity are different concepts: clicking a row does not mean owning or proposing a card.

When adding a filter, keep the full collection model intact. A visible search filter must not silently remove offscreen owned cards from optimizer input.

## 8. Tactical fusion planning

[`TacticalFusionPlanner`](../src/YfmCompanion.Engine/TacticalFusionPlanner.cs) starts from each eligible hand card and from each individual player-field target. It explores remaining hand slots recursively, appending a `TacticalStep` when a catalog fusion is valid. Consumed slot sets prevent the same physical copy being used twice.

A field-start route uses exactly one occupied field position. The field card interacts first, then the resulting monster can continue through remaining hand cards. A second field target cannot be smuggled into the same route. Ordinary first-pair symmetry is pruned where equivalent; later order is retained because intermediate results affect legality.

Compatible equips are terminal steps. An equip bonus does not survive a later monster fusion, so the planner does not carry it through subsequent fusion recursion. Invalid interactions that merely discard a card are not recommended as productive fusions. The glitch option controls eligibility at catalog resolution.

Results are deduplicated and ordered by effective ATK, effective DEF, consumed hand count, use of a field target and deterministic slot order. `TacticalModels.cs` carries routes, field zones, step kinds and effective values. This engine answers legal modeled routes; it does not choose hidden opponent cards or play the turn.

## 9. Exact opening-hand analysis

[`DeckAnalyzer`](../src/YfmCompanion.Engine/DeckAnalyzer.cs) accepts up to forty card IDs, canonicalizes them, validates IDs and evaluates hands of up to five cards. A complete 40-card deck has `C(40,5) = 658,008` physical hands. Duplicate copies remain distinct in the probability denominator.

Enumeration groups identical IDs. For a hand containing `k` copies from a deck group of `n`, its multiplicity includes `C(n,k)`; group weights multiply. Each distinct hand multiset is evaluated once and contributes its physical multiplicity. This accelerates duplicate-heavy decks without pretending all distinct multisets are equally likely.

Per-hand recursion records reachable fusion/equip results and a representative route, deduplicates each result within the hand and tracks consumed materials. Accumulation uses integer counts and sums. Probabilities are derived by dividing by the physical-hand total; exact comparisons use integer cross-products rather than rounded UI percentages.

The old fusion metrics describe modeled fusion/equip outcomes. `HandsWithAnyFusion`, threshold counts and `ExpectedBestFusionAttack` do not automatically include the printed ATK of every standalone monster. The newer body/setup metrics explicitly address strong natural monsters. Likewise, `DeadHandProbability` means no modeled fusion/equip; it is not interchangeable with the separate `NoMonsterProbability`.

## 10. Sampled analysis and uncertainty

`AnalyzeSampled` uses a fixed seed and samples physical positions without replacement within each hand. It sorts selected IDs before evaluation, so the same multiset has a stable cache key. Sampling can repeat a hand across trials. `SampleCount`, `IsExact` and a conservative normal-approximation proportion margin identify the result as sampled.

Sampling helps compare many candidate decks within a time budget. It does not become exact because a progress bar reaches the end of the sample. Final exact verification is a separate operation over all physical hands. Display estimated challengers separately from the best completed exact incumbent.

## 11. Support, terrain, equips and removal

The support analysis computes potential setups from cards actually available in the hand. A field/equip already consumed as a fusion material cannot also be counted as remaining support. Terrain modifiers come from `ForbiddenMemoriesStrategyEvaluator`; equip compatibility comes from the catalog. A preferred field in optimizer settings is a preference, not proof that the field is already active.

`HandsWith3500Setup` and `HandsWith4500Setup` are inclusive thresholds; `HandsWithEndgamePower` is strictly greater than 4,500 ATK. `HandsWith2800Body` counts a strong natural or reachable fused body. `HandsWithNoMonster` includes hands with no monster body, not merely low-ATK or zero-ATK defensive monsters.

Removal counters distinguish board clears from broader removal. Joint counters such as setup-or-clear count a hand once when either condition holds. Their probabilities must not be added as independent events. Raigeki and Dark Hole have different modeled tradeoffs; possessing removal is not a simulated successful duel.

[`GuideSupportStructure`](../src/YfmCompanion.Engine/GuideSupportStructure.cs) evaluates coherent deck-level fields and compatible equips for a target monster. It reports useful copies, the target and structure points. The one-field/two-equip target is meaningful only when those supports suit a reachable body; arbitrary spell quotas are not substituted for compatibility.

Setup availability may require multiple turns. The model does not simulate every opponent response, trap timing, draw sequence or survival requirement. This limitation appears in the UI and research documentation.

## 12. Parallelism, caches and determinism

`DeckBuildJob.MaximumWorkerCount` is bounded by eight and leaves capacity for the desktop/game on larger CPUs. Auto uses up to four candidate-search workers and up to eight analysis workers, reduced on smaller systems. Manual settings choose a supported count. The benchmark records explain why highest CPU percentage is not the performance objective.

Each analyzer worker owns mutable recursion buffers, stamps, accumulators and caches. The immutable catalog is shared. Parallel exact analysis partitions independent hand work, then merges results deterministically. Integer counts and deterministic representative-route selection preserve serial/parallel equality. Search candidate generation and result comparison remain coordinated; scheduling cannot redefine the frozen objective.

`BoundedAnalysisCache` accounts for entries and evicts within a byte budget. Parent/worker cache budgets are divided rather than giving each worker the entire original allocation. Accounting is a cache policy, not a measurement of total process RAM: the catalog, WPF images, stacks and runtime allocations are separate.

`DeckAnalyzer` serializes calls on one instance and checks cancellation while waiting for access. Progress is elapsed-time throttled, so multiplicity jumps do not skip all updates. Do not parallelize by sharing one mutable analyzer across worker tasks or by nesting an unrestricted worker pool inside every candidate evaluation.

## 13. Legal deck and virtual purchase space

[`DeckQuantitySpace`](../src/YfmCompanion.Engine/DeckQuantitySpace.cs) derives capacities from owned quantities, copy limits, special card restrictions and optional legal purchases. A legal deck has forty cards and fits all capacities. Virtual Star Chip purchases require valid numeric passwords, allowed costs, sufficient budget and the current redeemed-name exclusions.

The game does not provide reliable password-redemption history for every proposed purchase, so the user can exclude already redeemed names. The planner does not buy cards or write the save. A baseline feasible no-spend deck is retained unless a spending alternative is better under the same objective.

The quantity-space cursor uses large integers because combinatorial spaces can exceed normal integer limits. Resolution can skip a branch only when capacity/remaining-card/cost constraints make it impossible. A performance improvement must preserve this legality boundary and the mapping between cursor position and resolved work.

## 14. Candidate construction and search seeds

[`OwnedDeckOptimizer`](../src/YfmCompanion.Engine/OwnedDeckOptimizer.cs) prepares card assessments and candidate evaluations. `ForbiddenMemoriesStrategyEvaluator` supplies profile/field/type viability guidance and explanatory roles. The optimizer can create a legal seed, build variations, evaluate sampled or exact metrics, and describe included/excluded cards.

The campaign deck library adds six independent starting strategies. `CampaignDeckLibrary.Adapt` first takes legal available reference copies, then fills from a legal fallback until forty cards exist. It never invents ownership. The selected recommendation is a search start, not a locked final answer; the common objective can prefer another candidate.

`StarChipDeckPlanner` and `CampaignDeckOptimizer` are compatibility/public APIs for campaign/purchase planning. The interactive desktop's timed/proof workflow is orchestrated by `DeckBuildJob`. When changing scoring or legality, audit both paths so one cannot retain an outdated phantom-field assumption or purchase rule.

## 15. Frozen jobs and lifecycle state machine

[`DeckBuildJob`](../src/YfmCompanion.Engine/DeckBuildJob.cs) freezes ownership, options, contexts, budget and exclusions at creation. Its input identity incorporates frozen input, rule/objective versions and catalog identity. UI changes or a save watcher cannot mutate an active job's meaning.

```text
Preparing -> Searching -> Verifying -> Completed
    |            |            |
    +------------+------------+-> Pausing -> Paused -> resume
                 +--------------> Cancelled (retain best completed candidate)
Any active operation can fail while retaining the last usable result.
```

Quick, Balanced and Thorough use search budgets of five seconds, one minute and fifteen minutes respectively. Preparation and exact verification are reported as distinct stages; reaching the search budget is not permission to abandon an unfinished exact hand count and label it complete.

`Pause` cancels current work cooperatively and preserves state. Timed pause/resume is in-memory in that job instance. `StopAndKeepBest` returns the usable best found and ends the timed run. `VerifyBestAsync` upgrades a completed/stopped estimated candidate when allowed. `AdoptVerifiedIncumbent` checks input identity, legality, cost and exact 658,008-hand evidence before reuse.

The desktop installs only current-generation callbacks. An older run finishing after a new request must not overwrite the new result. `OptimizerActivityViewModel`, `OptimizerProgressPresentation` and `WindowActivityTracker` keep preparation/search/verification, busy controls, progress and cancellation consistent.

## 16. Objective ordering and score versions

[`DeckObjectiveComparer`](../src/YfmCompanion.Engine/DeckObjectiveComparer.cs) is shared by search and proof. Positive comparison means the first deck is better. For Balanced, ControlAndSafety and FieldAndType, coherent guide support participates first. Specialized profiles retain their distinct behavior.

The current guide numerator weights physical-hand counts: 35 for 3,500-setup-or-clear, 20 for a 2,800 body, 15 for endgame-power-or-clear, 10 broad removal, 10 a 3,500 setup, 5 endgame power, 5 board clear and minus 15 no-monster. Deck structure points are added on the same normalized scale. These are explicit heuristics, not fitted win probabilities.

Campaign comparison considers safe-opponent count, worst modeled matchup, opening-answer coverage and heuristic safety; the gauntlet tie-break uses fixed bins before recovering exact primary ordering. Power metrics, lower required Star Chips and stable card IDs complete tie-breaking. Integer/large-integer comparisons avoid pairwise epsilon rules that can become non-transitive.

If semantics change, update objective/rules identity constants and tests. A saved proof from an older objective must fail closed. Display wording alone does not require inventing a new scoring identity.

## 17. Durable proof and checkpoint recovery

[`DeckProofSearch`](../src/YfmCompanion.Engine/DeckProofSearch.cs) enumerates the legal quantity space and exactly evaluates each valid candidate. It may prune impossible branches, but sampled estimates cannot certify an unevaluated legal branch as inferior. `ProvenOptimal` is true only when the legal space has been exhausted and a best deck exists.

Checkpoints contain schema/rules/objective/catalog/request identities, the frozen request, cursor, legal-deck count, elapsed time and a completed exact incumbent. A checksum envelope detects partial/corrupt data. Writes use a temporary file and replacement, and counters advance only after a complete evaluation or sound branch skip. Cancellation saves the last completed position; the interrupted leaf is evaluated again on resume.

On resume, all identities and counter ranges are checked. An incumbent must still be a legal forty-card deck, have the recorded cost and carry exact hand evidence. `OptimizationReportSnapshot` freezes nested collections so reports cannot be mutated after identity checks. `ProofCheckpointFiles` handles desktop inspection, backup and fresh-checkpoint choices; the proof confirmation window makes the selected path and operation visible.

Never fix a mismatched checkpoint by editing its version/hash fields. Preserve the old file and start a new proof. For recovery changes, run interrupted-write, corruption, changed-input, purchase-space and real-catalog recovery tests.

## 18. Campaign research and opponent modeling

[`CampaignResearchData`](../src/YfmCompanion.Engine/CampaignResearchData.cs) loads and validates guardian cycles, opponent pools, scope policy and source manifests. Validation covers expected pool totals, card references, opponent scopes and guardian relationships. `CampaignOptimizationContextBuilder` turns the selected general campaign, single opponent or gauntlet into a frozen scoring context.

General campaign and final gauntlet are explicit sets, not inferred from UI order. Research provenance includes upstream revision/input hashes. Static reference facts and heuristic model choices are separate: a sourced opponent pool does not make every modeled fusion opportunity an observed probability.

`OpponentThreatEvaluator` enumerates direct and material-limited chained threats from the opponent pool. `OpponentSafetyScoring` combines reachable answers, terrain, possible stars, removal and importance weights into a model assessment. Unknown CPU guardian choices are alternatives to consider, not a guessed live star.

To change opponent data, update source/provenance JSON, regenerate derived data through `tools/Phase1Research`, run its invariant verifier, then run the campaign tests and acceptance probes. Do not edit a generated threat summary while leaving the underlying pool unchanged.

## 19. Recommended deck library

The app-authored blueprints live in [`campaign-decks.json`](../src/YfmCompanion.Engine/Research/campaign-decks.json), embedded in Engine. Each has a stable ID, name, hero-card icon, accent, stage, plan, cautions, required entries, example recipes and source links. They are informed by community research and constrained to the vanilla catalog.

`CampaignDeckLibrary.Load` validates unique IDs/entries, forty total cards and copy counts. `ForCatalog` excludes a blueprint when its required cards or icon are absent from a supplied catalog. `OwnedCopies` sums `min(required, owned)` by card ID, counting physical copies rather than names or hypothetical fusion products. Owning extra unrelated cards cannot inflate the displayed `x / 40`.

`CampaignDeckLibraryWindow` displays six artwork buttons and updates ownership/missing counts. Its exact check analyzes the complete reference list, including cards the player lacks, and is cancellable. Adapting a build selects an owned-card search start. Active or paused jobs protect their frozen starting strategy; collection refresh is queued safely.

The six themes, research methodology, acquisition limits and example metrics are documented in [fan deck research](research/FAN_DECK_RESEARCH.md). The library is a practical set of strategies, not an exhaustive collection of every fan deck or a guarantee of campaign completion.

## 20. Save parsing and collection ownership

[`Ps1MemoryCardReader`](../src/YfmCompanion.RetroArch/Ps1MemoryCardReader.cs) accepts supported raw 128/256 KiB memory-card images. It checks the memory-card directory/block structure and the NTSC-U save identifier, validates redundant copies and card ranges, and parses deck, chest, flags, unlocked duelists and Star Chips. Invalid auxiliary currency is withheld instead of becoming a large spendable budget.

Parsing and the content hash use the same bytes. A stable-read retry protects against a file changing during an emulator flush. The parser can preserve a valid chest when the constructed deck is empty or incomplete; that is not permission to load an invalid forty-card deck into the analyzer.

`RetroArchSaveLocator` discovers candidates from supported locations and inspects content rather than trusting extension alone. `CollectionSnapshotService` supports AutomaticNewest, PinnedFile and Manual. A valid newest file is selected deterministically; rejected newer candidates are reported. Missing/unreadable input can retain the last validated collection as stale, with its reason.

`SaveModels.cs` and `CollectionSnapshot` distinguish chest quantities, deck copies, total ownership, timestamps, content identity and freshness. Save changes during active/paused work do not rewrite frozen optimizer input. Manual quantities are explicit local preferences and must not be overwritten by background discovery.

## 21. Read-only UDP transport

[`IRetroArchReadClient`](../src/YfmCompanion.RetroArch/RetroArchNetworkClient.cs) exposes status, core-memory reads and core-RAM reads. The concrete client restricts the endpoint to loopback, validates ports/lengths, chunks reads, uses bounded retries/timeouts and serializes requests with a semaphore. Response parsing checks the expected command/address/length before returning bytes.

The public API intentionally has no arbitrary-command sender. `GET_STATUS`, `READ_CORE_MEMORY` and `READ_CORE_RAM` are the allowed operations. Read-only protocol tests validate both normal responses and malformed/truncated responses. A new transport implementation must satisfy this interface and the same validation tests.

`RetroArchConfigInspector` reports the configured command port/listener situation. It never enables Network Commands or rewrites `retroarch.cfg`. The user configures RetroArch. The app's loopback restriction and the emulator's own listener/firewall scope are distinct.

## 22. Live decoding and transient state

[`ForbiddenMemoriesLiveReader`](../src/YfmCompanion.RetroArch/ForbiddenMemoriesLiveReader.cs) validates compatible content/status, discovers the supported read mode and decodes the known NTSC-U layout. Live model records separate hand order, monster/spell zones, player/opponent fields, terrain, Life Points, constructed/shuffled decks and save availability.

The screen-mode byte identifies an active duel; a changing action word is not used as a general screen detector. Startup may briefly have incomplete shuffled-deck memory, so that structure is diagnostic rather than a second duel gate. Positive Life Points and compatible content still matter.

Advice-relevant state is read twice. If hand, field or mode changes between reads, the reader throws a transient-state exception. The desktop reports the transition and retries on the next one-second tick. Validated collection/deck browsing can be retained, while unsafe actionable hand/field advice is withheld. A real disconnect clears live-only state.

The live poll has a non-overlap guard. `StableCardItems` reconciles refreshed rows without destroying selection/scroll when identity is unchanged. Tests cover changing snapshots, transient reads, recovery and disconnection separately; a paused read is not evidence of correct unpaused changing-board behavior.

## 23. Guardian stars and compact presentation

`GuardianStarRules` owns the verified directed cycles and relationship classification. `GuardianStarPresentation` formats both possible stars and known matchups. `CompactLivePresentation` converts tactical/live data into narrow display rows and preserves unknown enemy star/position as unknown.

Compact Live is bounded to 272 by 1002 logical window units in the current desktop implementation. Rendering, window chrome and OS scaling affect physical screen pixels; scaled screenshot tests are not a native OS-DPI switch test. The columns are Result, ATK and Route. Names wrap to at most two lines, and guardian symbols are 20px in the Result presentation. Width allocation reserves space for stats/routes and disables sideways scrolling.

Warm and red presentation states distinguish selectable fusion stars and known opponent relationships. Color is accompanied by symbols/order/tooltip information. Routes retain physical hand numbers and `F(n)` field positions. `PIN` changes topmost state; `FULL` restores the main workspace.

## 24. WPF views, control ownership and layout

`MainWindow.xaml` owns the global header, workspace tabs, scroll region, optimizer activity dock and compact view. The visible tabs are Turn Adviser, Deck Analyzer, Live Duel and Owned-Card Optimizer. Each has a view under `Views/`; view code-behind forwards user events to the controller rather than reimplementing engine logic.

`MainWindow.TabControls.cs` registers/aliases named controls across those view namescopes. This is a deliberate compatibility seam in the current controller design. Change both the XAML and aliases when renaming a control. The retained internal SaveSnapshot view supplies shared legacy snapshot controls; it is not a fifth visible tab. New user-facing save actions belong in analyzer/optimizer flows.

`MainWindow.xaml.cs` still contains substantial orchestration. It coordinates cancellation, source installation, job generations, live updates, displayed reports and window modes. Split a subsystem only with lifecycle tests that cover late results and closing; moving methods between files alone does not isolate state ownership.

`AdaptiveColumnsPanel` handles responsive grouping. Owned collection/results can stack when narrow. Data grids and scroll containers have distinct responsibilities: the compact grid must fit horizontally, while the full workspace preserves navigation and vertical scrolling. Inspect real rendered controls at intermediate scroll positions, not only the top of each tab.

## 25. Artwork, icon and memory use

All 722 card images are pinned to an attributed upstream revision. The artwork import tool converts source WebP to PNG without changing decoded pixels, validates card-number/name mapping and writes manifests. Artwork rights are separate from code licensing; retain the notices and metadata license.

`OwnedCardRow.RefreshArtwork` resolves explicit custom file, custom folder and bundled fallback. Missing/unreadable overrides recover to bundled artwork. `ThumbnailCache` decodes a small frozen bitmap, uses an LRU with a byte budget and invalidates entries on file length/modified-time changes. Stream loading avoids WPF's independent URI cache retaining replaced images.

The original application icon lives in `assets/branding/app-icon.svg`. `Create-AppIcon.cjs` renders a preview and ICO frames at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels. The project embeds it in the Windows executable and WPF window resources. It is an original Egyptian card/seal motif inspired by the game's atmosphere, not a copied game logo. Editing the SVG and regenerating the ICO is sufficient; normal builds need no Node image tooling.

## 26. Preferences, progress and diagnostics

`DesktopSettingsStore` uses JSON under `%LOCALAPPDATA%/YFM Fusion Companion`, with an explicit environment override for isolated audits. It reads defensively and writes through a temporary file followed by replacement. Settings include geometry, topmost/compact mode, save source, artwork overrides, manual quantities, CPU choice, selected reference strategy and suggested-card order.

Suggested card order defaults to Alphabetical. Card number sorts ascending; ATK/DEF descend with name/ID ties. `CollectionViewSource` sorts presentation rows, leaving report order, metrics, purchases and frozen requests unchanged. Selection is restored only when it matches a supported option, and incoming reports reapply it.

`OptimizerProgressPresentation` differentiates unknown-duration activity, timed search budget and exact hand progress. A completed worker sample does not fill the whole search-budget bar. Completion is displayed only after the corresponding result is installed.

`LocalDiagnosticLog` keeps bounded status/error records and supports an explicit export. It does not automatically transmit information or dump card, hand, deck, collection or emulator memory values. Keep those exclusions when adding diagnostics. Release verification logs and developer benchmarks are separate from player diagnostic exports.

## 27. Testing and audit layers

| Layer | Entry point | What it establishes |
|---|---|---|
| Unit and real-catalog regression | `dotnet test YfmFusionCompanion.sln -c Release` | Rule, parser, count, ownership, deterministic search, proof and recovery behavior |
| Independent release probes | `YfmCompanion.Phase4Audit` | Cross-cutting counterexamples such as phantom field, invalid purchases and malformed research |
| Production UI rendering | `YfmCompanion.UiRender` | Real XAML initialization, artwork, library actions, sorting and compact fit |
| Desktop lifecycle | `YfmCompanion.UiAudit` | Start/pause/stop/verify, source races, closing, selection/scroll and keyboard interactions |
| Visual state matrix | UiAudit `--states` | Empty/populated/loading/error/completed at multiple logical sizes/scales |
| Live observation | UiAudit `--real-live`, AuditRunner, LiveProbe | Read-only environment-specific integration; report paused/playing and changes separately |
| Final executable | `--verify-package` plus normal startup | Packaged resources can be loaded by the actual binary |
| Archive/public delivery | Publisher plus public hash comparison | Extracted and downloaded bytes match the accepted artifact |

All twelve projects are in the solution so no audit silently uses a stale unbuilt executable. No single row proves every other row. Benchmark timings depend on CPU, cache warmth, collection and competing work; include fixture/worker counts and correctness equality with a speed claim.

## 28. Reproducible release process

`tools/Publish-Release.ps1` is the single local/CI publisher. It reads version metadata from the desktop project, refuses to overwrite an existing target, performs locked restore, formatting, warning-free build, tests, dependency scan, deterministic catalog rebuild, research checks, acceptance probes, production UI/lifecycle/artwork checks and documentation generation.

It publishes into a fresh directory, organizes resources, starts the exact executable's offline self-check, smoke-starts the ordinary app with isolated preferences, extracts its Windows icon, and verifies source files did not change during packaging. A manifest covers package files; the ZIP is independently extracted and every file/hash checked. The external SHA-256 identifies the final ZIP without a circular self-hash.

GitHub Actions uses the same script. Tag/version disagreement must fail before release creation. Release assets consist of one player ZIP and its checksum; documentation is inside. Source is accessible through the repository and GitHub's source archive. Keep audit summaries and cleanup inventories in Git, not every historical runtime folder or downloaded archive.

Delete an older release/package only after the replacement, GitHub checks and public download pass. Preserve source commits/tags and concise audit logs so a regression can still be traced without keeping many copies of the runtime and artwork.

## 29. How to change the application safely

| Desired change | Start here | Checks to run |
|---|---|---|
| Add or correct a fusion/equip | SQL source, Data importer/catalog | Import reproducibility, catalog golden pairs, tactical/analysis tests |
| Change hand metrics | DeckAnalyzer + DeckAnalysisModels | Independent small-deck enumeration, duplicate weights, serial/parallel equality |
| Change balanced priorities | DeckObjectiveComparer + GuideSupportStructure | Comparator order/transitivity, support consumption, proof identity/recovery |
| Add a reference deck | campaign-decks.json + CampaignDeckLibrary | Forty legal copies, reachable recipes, ownership counts, UI/reference checks |
| Add an opponent/rule source | Research JSON/generator + context builder | Research invariants, malformed-source probes, threat/safety tests |
| Change live detection | ForbiddenMemoriesLiveReader | Synthetic boundary/transient tests, read-only real changing-board evidence |
| Support another save format | Ps1MemoryCardReader + SaveModels | Independent fixtures, empty deck/chest cases, before/after source hashes |
| Change a screen/control | Its XAML, forwarded events, MainWindow aliases | Production render, lifecycle, keyboard and narrow layouts |
| Add a user preference | DesktopSettings and controller restore/save | Missing/default/corrupt settings, persistence, isolated audit |
| Change parallel execution | DeckAnalyzer or DeckBuildJob | Exact equality, stable ordering, cancellation, progress, cache budget, measured speed |
| Change the Windows package | Publish-Release + RuntimeResources | Fresh output, packaged self-check, missing-resource failure, extraction/public hash |

Prefer a small independent counterexample test over a test that repeats the implementation. Keep pure engine code reusable without WPF. Update this guide and regenerate the source index when entry points move.

## 30. Troubleshooting by symptom

**Database could not load:** inspect `Resources/Data/yfm.db`, `RuntimeResources`, `FusionCatalog.Load` and the deterministic build report. Emulator availability is not a database prerequisite.

**Ownership changed unexpectedly:** inspect source mode, content identity/freshness, pending source refresh and frozen job input. Do not debug this by overwriting the current save with an old snapshot.

**A deck metric changed with worker count:** compare integer totals and representative results using CpuParallelismTests before timing anything. Look for shared scratch state, merge ordering or incomplete cancellation.

**Optimizer seems stuck:** identify preparation/search/verification/proof state and progress timestamps. Exact verification can outlast a search budget. A proof space may be enormous. Preserve the checkpoint and report observed stage; do not claim optimality from elapsed time.

**Live advice disappears briefly:** distinguish a transient double-read mismatch from no-content/wrong-game/disconnection. A safe retry is expected during changing state; retained browsing is not retained permission to use stale hand positions.

**Artwork is missing:** inspect explicit override, custom folder, bundled resource root and manifest hashes. Then test cache invalidation and PNG decoding. Ownership has no dependency on a custom-art folder.

**A control works in a test but the screenshot is blank:** check actual tab selection and named view registration. The removed fifth-tab audit bug is a concrete example of why invisible-control interaction is not visual acceptance.

## 31. Known limits and extension boundaries

Live decoding is scoped to the validated NTSC-U game through RetroArch/SwanStation. Other regions, mods and cores require new evidence and explicit support. Rituals are experimental; undocumented timing/effects are not invented. Opponent-response simulation, hidden cards and future draws are outside exact opening-hand analysis.

The app is English-only and has no automatic updater, telemetry service or cloud optimizer. User settings and proof files are local. Full-window orchestration still has a large controller and legacy snapshot-control aliases; contributions should improve these with lifecycle coverage rather than disguising them as a fully separated MVVM architecture.

The code is MIT licensed. Game names/rules and bundled card art are not relicensed as MIT. Consult [third-party notices](../THIRD_PARTY_NOTICES.md) before redistributing assets. The original application icon source is covered by the repository license.

## 32. Complete source navigation

Open the [generated source index](reference/SOURCE_INDEX.md) for every production file and indexed declaration/named XAML control. It includes Data, Engine, RetroArch and Desktop, plus the test and tool entry-point map. Each entry links to the source line from which it was generated. Regenerate it with `python tools/Build-Documentation.py`; do not manually maintain line numbers.

For implementation history use `git log -- <path>` and `git show <commit>:<path>`. Release/cleanup records under `docs/audit/releases/` and historical checkpoint notes preserve the reasons and evidence without requiring an old extracted binary package.

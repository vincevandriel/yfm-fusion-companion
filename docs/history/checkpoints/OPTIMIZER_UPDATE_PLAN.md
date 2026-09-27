# Optimizer and visual redesign implementation plan

Approved scope: native, offline, English-only Windows companion. Preserve game/save read-only access, Hardcore compatibility, canonical concrete fusion rules, field-first chains, terminal equips, and narrow Compact Live numeric/F(X) routes. Do not replace the installed release before the audit and release gates pass.

## Phase/model gates

| Phase | Required model | Reasoning | Deliverable |
| --- | --- | --- | --- |
| 1 | GPT-6 Astra (`gpt-6-astra`) | High | Performance, shared scoring, timed search, honest progress/cancellation, exhaustive proof and durable resume |
| 2 | GPT-5.6 Sol (`gpt-5.6-sol`) | Medium | Automatic newest save, owned-card gallery, five-tab redesign |
| 3 | GPT-6 Astra (`gpt-6-astra`) | xhigh | Independent correctness, performance, integration and visual audit |
| 4 | GPT-5.6 Luna (`gpt-5.6-luna`) | Medium | Documentation, illustrated PDF, packaging and public-download verification |

Mandatory checkpoint and pause at every phase boundary. Never infer that a test build passes the complete phase gate. Development-model choices are not player-facing deck-building modes. No paid AI/API/network dependency in deck building.

## Phase 1 specification

1. Preserve a baseline before engine changes. Synthetic starter/medium/near-complete, duplicate/diverse collections; manual/general/specific goals; chips on/off. Record stage time, first legal result, allocations, peak memory, cancellation and concurrent one-second live-reader behavior. Mark unavailable live coverage unverified.
2. Versioned transitive comparator shared by owned search, purchase plans, incumbents and proof. Primary ordering: modeled opponent coverage, weakest matchup, opening answer coverage, average counter score, strong-fusion probabilities and expected ATK. Where a secondary gauntlet goal exists, fixed 0.005 probability/25 ATK bins precede its tie-break, then exact primary metrics, lower spending, canonical IDs. Exact rational hand metrics retain total best-ATK sum. Modeled safety is not a win probability.
3. Request-scoped assessment precomputation; bounded shared hand/deck cache (256 MiB search; separately 64 MiB thumbnails in Phase 2); comparison evaluated once; reuse purchase bundle work. Lightweight common sampled/exhaustive evaluator; detailed routes only for displayed reports. Cancellation inside costly loops. At most max(1,min(4,logical CPUs/2)) workers with deterministic reduction.
4. Quick: 5 s search, estimated sample count/uncertainty, explicit Verify. Balanced: 60 s and up to 2 exact finalists. Thorough: 900 s, restarts and up to 8 exact finalists. Preparation/verification are extra visible stages. Early legal candidate, incremental improvement, compatible verified incumbent protected from regression.
5. Frozen inventory/source/options/rule/objective identity; preparing/searching/verifying/pausing/paused/completed/cancelled/failed states. Stage-specific actual work, elapsed, credible ETA range, candidates and best result. Separate exact/estimated and best-found/proven labels. Approximately 5 progress updates/s, unknown-total activity without invented percentages. Pause/Resume/Stop-and-keep-best. UI reaches Ready only after result installation in Phase 2.
6. Proof enumerates every feasible 40-card quantity vector, no heuristic exclusions. Chips: deficits, eligibility, exclusions, one purchase/name, copy and budget limits. Only sound capacity/budget pruning. BigInteger capacity-space count, resolved-space versus legal-decks counts. Feasibility/duration preview. Atomic versioned checkpoints with hashes, cursor/random state/counters/incumbent. Reject incompatible inputs. Only complete enumeration proves optimal for the selected model and inputs, not actual duel win rate.
7. Gate: independent small-case brute force, cache equivalence, 658008 hand accounting, progress/cancel in all expensive stages, measured before/after benchmarks, preserved fusion/ownership rules. Any missing item keeps Phase 1 open.

## Phase 2 specification

Shared snapshot service; Automatic newest validated supported save by modification time/deterministic tie-break. Remembered path is only a candidate. Search active/configured known locations, core/content subfolders and user-selected folders; validate contents of renamed files. Choose file, Refresh and explicit pinned Use this file. Stable reads/debounced folder watchers; corruption/missing sources explained, retained data marked stale. Chest plus deck is ownership, not seen cards. Empty/partial deck retains chest. Refresh at startup/tab entry/manual refresh. Frozen running request; pending newer save and protected labelled manual edits.

Owned-only virtualized card tiles: ID/name/type/guardian symbols/ATK/DEF/quantity/deck inclusion; search/filter/sort/add-autocomplete. Optional local ID-indexed PNG/JPEG artwork, folder/per-card selection, bounded thumbnails, attractive fallback, no personal art in releases.

Modern Egyptian design: navy/lapis/white with restrained gold, readable states/contrast, shared tiles/slots/inspector/source/metrics/progress components, tab views/view-models rather than more monolithic handlers. All five tabs redesigned: optimizer gallery/goals/speed/progress/result; analyzer tray/probabilities/routes; adviser ordered hand/field slots; Live stable cards/routes/guardian/inspector; Save visual deck/collection with advanced validation. General/Specific/Gauntlet/Custom goals separate from speed. Advanced proof. Purchase counts/reasons/40-card completeness and truthful labels. Wide side-by-side/narrow stacked. Preserve selection/scroll and Compact Live width/no-min-size/numeric routes.

Gate: source-state tests, owned-only counts, manual edit protection, all-tab keyboard navigation, real responsive optimization, 1366x768 and 1920x1080 screenshots at 125/150/200% scaling.

## Phase 3 specification

Independent review against every requirement, not earlier completion claims. Audit renamed/new/partial/missing/stale saves; gallery/art/cache/keyboard; all job transitions including restart; legality/purchases; transitivity and independent proof oracle; exact/estimated/optimality labels; fusion/field/equip/guardian/Compact regressions; all tab states. Reference-machine goals: busy <200 ms, no unexplained progress gap >1 s, pause/stop <1 s, ordinary first candidate within Quick budget, responsive non-overlapping live polling, measured cache reuse and memory caps. Publish measured before/after with limitations. Fix in responsible phase and repeat affected tests. Unavailable live coverage is not a pass.

## Phase 4 specification

After audit pass, update Markdown README, illustrated PDF, screenshots, release notes, dependencies and limitations. Explain saves, manual edits, budgets/progress/pause/resume/verify/proof. Use v1.2.0 if unused or next unused minor. Preserve previous installed release in Previous Versions. Run release workflow, independently download/hash/extract, check dependencies/start executable, exercise save import/Quick/progress/Compact from downloaded package. Return algorithmic failures to earlier phase.

## Handoff protocol

Maintain this plan and OPTIMIZER_UPDATE_CHECKPOINT.md. Every pause records base/current commit, completed and remaining work, exact test/benchmark evidence and failures, next task/model/effort, running processes and optimizer checkpoint state. Use: `Paused after Phase [N], [gate]. Resume with [model], reasoning [level]. Next action: [task]. Checkpoint: [link].` Within-phase interruption must explicitly say the phase gate has not passed.

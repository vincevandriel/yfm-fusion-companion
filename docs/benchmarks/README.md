# Optimizer update — Phase 1 working evidence

These are Phase 1 development measurements, not a release announcement or a universal performance guarantee. Consult `OPTIMIZER_UPDATE_CHECKPOINT.md` for the authoritative gate status. Desktop wiring and visual acceptance belong to Phases 2 and 3.

## Reproducible harness

The source is `tools/YfmCompanion.OptimizerBenchmarks`. It builds the catalog from the repository SQL and constructs synthetic inventories. No personal save is used. The legacy-API matrix covers four inventories (starter duplicate-heavy, starter diverse, medium, near-complete), three contexts (manual, specific opponent, general campaign), Star Chips off/on, and cold/warm calls: 48 measurements.

Run from the repository root:

```powershell
dotnet run --project tools/YfmCompanion.OptimizerBenchmarks -c Release -- . docs/benchmarks/local-matrix.json --short --matrix --quiet
```

`--short` requests cancellation after **two seconds per case**. This is a bounded comparative diagnostic, not the user-facing Quick/Balanced/Thorough budget. Omitting it permits 30 seconds per case. Preparation of the catalog, optimizer instance and campaign contexts is outside these legacy-API case timings; measure the new job interface separately for end-to-end latency. Process peak working set is a process-lifetime maximum, not isolated per-case memory. Allocations are total allocated bytes during a case, not retained cache size.

The baseline matrix used a separately preserved pristine tree at commit `a967903`, with the compatible matrix harness copied into that tree before its engine was built. For a fresh baseline, export that commit to a separate directory and copy only this tool directory into it. Build/run there with `-p:LegacyBaseline=true` to exclude the new live-job branch while retaining the same matrix code. Never overwrite the working engine with old files to obtain a baseline.

## Before/after results on this machine

Machine: 12 logical processors, Windows, .NET 9; exact runtime and timestamps are recorded in each JSON. One cold and one warm measurement per case; these are not repeated statistical trials.

| Matrix outcome with two-second cancellation request | Pristine v1.1 source | Foundation | Hardened | Final gate |
| --- | ---: | ---: | ---: | ---: |
| Completed | 14 / 48 | 38 / 48 | 38 / 48 | 42 / 48 |
| Cancelled at diagnostic budget | 34 / 48 | 10 / 48 | 10 / 48 | 6 / 48 |
| Largest progress gap | 12,911 ms | 1,158 ms | 257 ms | 258 ms |
| Largest cancellation overrun after 2 s request | 10,912 ms | 105 ms | 32 ms | 21 ms |

Files: `optimizer-baseline-matrix.json`, `optimizer-foundation-matrix.json`, `optimizer-hardened-matrix.json`, and `optimizer-gate-matrix.json`. These compare complete planner calls; the corrected comparator and sample implementation can choose different candidates, so do not interpret the timing ratio as an isolated evaluator speedup. A two-second timeout is not an illegal deck or a claim that normal Quick mode cannot finish. The intermediate `optimizer-final-matrix.json` remains as the measurement before the last scratch-reuse and purchase-cache refinements.

The diverse starter/manual/no-purchase case illustrates both reuse and its cost. In the matrix baseline, cold/warm calls took 1,523 / 1,512 ms and allocated roughly 17.8 MB each. Final calls took 1,070 / 8.7 ms, allocating 124.7 / 0.96 MB. Not caching empty hand outcomes reduced the initial foundation's roughly 213 MB cold allocation, but the final cold call **still allocates much more than baseline**. This is an explicit memory-for-reuse tradeoff, not a universal improvement. Final near-complete/general/purchases cold and warm calls completed in 1,466 / 1,076 ms, allocating 477 / 42 MB; the earlier hardened versions both exceeded the two-second budget. Some diverse purchase searches still allocate over 1 GB across their operation: allocation volume is not retained memory.

The combined retained-search-cache accounting is capped at **256 MiB**: 224 MiB for hand/deck results plus 32 MiB for request-specific strategy, pair, terrain and opponent-counter values. Accounted entry sizes include conservative payload/container allowances; this is **not** an OS working-set cap or an exact managed-heap measurement. Unit tests exercise eviction/equivalence and the aggregate limit. Thumbnail caching is a separate Phase 2 responsibility. Matrix working-set figures are process-lifetime peaks; do not attribute a peak to the last individual case or confuse it with cache occupancy.

Four foundation matrix cases recorded progress gaps just over one second (approximately 1.00–1.16 seconds). Candidate generation and purchase scoring then gained internal progress/cancellation hooks; the hardened and final matrices verify those fixes. Earlier files retain their original results and are not relabelled as final-build measurements.

## Live-reader coexistence

```powershell
dotnet run --project tools/YfmCompanion.OptimizerBenchmarks -c Release -- . docs/benchmarks/local-live.json --live
```

This performs read-only snapshot polling once per second alongside a real 60-second Balanced search on a synthetic near-complete collection, followed by exact finalist verification. It never sends controls or changes saves. Output retains timing, success, playback status and a duel-active boolean only—no card IDs, personal deck, file paths or raw game memory. Polls are sequential; maximum concurrent reads is measured. The artifact does not replace WPF UI/responsiveness testing.

The first live run completed with 69 successful reads, zero failures, maximum concurrency one, and exact 658,008-hand finalist analysis. It exposed a roughly 785 ms delay before initial progress due to synchronous catalog fingerprinting; that work was moved behind the initial background-job progress notification. `optimizer-live-coexistence.json` contains the subsequent rerun. Inspect `ActiveDuelReads`: zero means active-duel coexistence is still unverified even if connection polling passes.

That Balanced rerun delivered initial progress in 48 ms but exposed a 1.42-second preparation gap. Explicit preparation-stage notifications were added. The post-fix **active-duel Quick** run (`optimizer-live-quick.json`) then recorded **14/14 successful active-duel snapshots**, zero errors, maximum concurrent reads one, first progress **46.8 ms**, first usable deck **1,494 ms**, maximum progress gap **728 ms**. Quick correctly returned sampled, not exact, statistics. Thus active-duel Quick coexistence is verified; the 60-second Balanced run was outside an active duel. Full-mode WPF rendering and long-duration active-duel stress remain separate acceptance work.

The full **900-second Thorough** run (`optimizer-live-thorough.json`, add `--thorough` to the live command) completed with **908/908 successful active-duel reads**, zero failures and maximum overlap one. First progress was **48.6 ms**, first usable deck **1,031 ms**, and maximum progress gap **766 ms**. The result was exactly analyzed over **658,008** physical opening hands. This run was made before the final proof-incumbent and assessment-cache changes; it is evidence for the full-duration search/live path, not a measurement of those later fixes. During the run, observed working sets ranged approximately 212–232 MiB; these point samples do not establish a hard maximum.

The subsequent final-build Balanced artifact is `optimizer-final-live-balanced.json`. Schema 2 adds stage names, search-time consumption, candidate counts, cache accounting, allocation volume including live polling, and process-lifetime peak working set. Check the recorded values directly. Native WPF rendering remains unverified until Phase 2 integration.

Further profiling separated rejected-candidate scoring from displayed explanations (`optimizer-lean-live-balanced.json`), then reused the analyzer's scratch arrays (`optimizer-reuse-live-balanced.json`). For the same 60-second search budget, the full-report run examined 145,172 candidates and allocated 33.77 GB cumulatively; the final scratch-reuse run examined 404,580 candidates and allocated 11.43 GB, including live polling. That is about **66% less total allocation while doing more search**, not a claim of a smaller allocation for every individual deck or a universally superior deck. All intermediate evidence remains available.

The scratch-reuse Balanced run recorded **68/68 successful active-duel reads**, zero errors, overlap one, initial progress **46.6 ms**, first deck **994 ms**, and maximum progress gap **752 ms**. Two finalists received exact analysis; the winning report accounts for **658,008** hands and explicitly has `ProvenOptimal: false`. Its accounted search cache was **228.2 MiB of 256 MiB**, and observed process-lifetime peak working set was approximately **213.1 MiB**. The difference reflects conservative cache accounting, not a claim that object accounting measures physical RAM exactly. The final purchase-planner cache sharing does not run in this manual near-complete live fixture and is covered by the gate matrix and regression suite.

`optimizer-reuse-matrix.json` reruns all 48 cases after scratch reuse (42 completed, 6 diagnostic cancellations, maximum progress gap 250 ms). `optimizer-gate-matrix.json` is the last matrix after sharing prepared opponent assessments with password-purchase scoring. Use the latter for the final Phase 1 gate; all earlier files are development history.

## Correctness evidence and remaining acceptance work

- Existing canonical/field/equip/save/guardian regressions retained.
- Independent physical-hand oracle agrees with cached and uncached analysis.
- Integer ATK totals and weighted 658,008-hand accounting retained.
- Comparator antisymmetry/transitivity tested around bin edges; numeric IDs and spend tie-breaks tested.
- Capacity enumeration compared against independently generated purchase vectors.
- Proof tested against all 105 legal decks in a normal three-copy synthetic inventory and a closed-form fusion-probability oracle.
- Checksummed checkpoint corruption, pause/resume and changed-input rejection tested.
- Timed Quick, stop-and-keep-best, verification, frozen inputs and incumbent preservation tested.
- Initial-preparation pause writes a durable schema-2 proof checkpoint; exclusive leases prevent two jobs overwriting it.
- A real-catalog interrupted partial leaf resumes without counting unfinished work; the completed result agrees with uncached analysis.
- Verified incumbents survive an immediate proof pause and a fresh job's checkpoint recovery. A regression first reproduced the lost-incumbent failure, then passed after the fix.
- Proof purchase winners match independent closed-form expectations at different budgets and with case-insensitive redemption exclusions; infeasible spaces never claim an optimal deck.
- Frozen campaign/purchase inputs survive caller edits. Opponent scoring agrees with independent numeric expectations, reuses cached values, and separates changed contexts.
- The current comparison deck is analyzed once after purchase alternatives; a progress-count regression verifies this rather than relying only on cache retention.
- Reused analyzer scratch is checked against an intervening dead deck to prevent count leakage and mutation of earlier reports.
- Proof preflight exposes full BigInteger capacity counts and an explicitly qualified measured work extrapolation, not a proof or a guaranteed finish time.

The desktop redesign and progress panel belong to Phase 2 and have not begun. In-memory pause/resume for timed modes must not be advertised as durable across app restarts; proof mode provides durable restart. The separate Phase 3 audit must independently recheck integration, public labels, cache behavior, real UI responsiveness and all visual/source workflows. `PHASE2_ENGINE_CONTRACT.md` records the precise engine/UI handoff.

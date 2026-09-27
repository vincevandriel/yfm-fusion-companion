# Deck optimizer CPU parallelism

The optimizer now evaluates sampled candidates with bounded CPU workers and parallelizes exact hand verification. Each worker owns its analyzer scratch arrays, route cache, assessment cache and cancellation state. The catalog is shared as read-only data. The coordinator generates candidate decks and commits completed results in generation order. Exact analysis partitions the original enumeration order and merges integer counts and route representatives in that order.

Choose **CPU WORKERS** under **Advanced Strategy Settings** in the Owned-Card Optimizer. **Auto uses four workers for every candidate search and eight for exact verification.** The standalone Deck Analyzer also uses eight. On smaller CPUs, counts are reduced to at most the logical processor count minus two, with a minimum of one. Manual values from one to eight use the selected count for both optimizer stages; choose Auto for the four/eight split. The setting persists between launches and is frozen for the current job. Existing explicit manual choices remain overrides.

The aggregate retained search-cache budget remains at most 256 MiB. Exact enumeration temporarily stores packed distinct hands (at most 658,008 items); this temporary allocation is additional to the retained-cache budget. Completed reports do not reference worker scratch arrays. Pause and stop cancel active workers, join them, and retain completed candidates. Resume reuses the worker caches. Interrupted exact analysis does not install a partial report.

Proof traversal and durable checkpoint schemas are unchanged and remain serial. Selecting CPU workers does not change the frozen scoring/input identity or invalidate a proof checkpoint. Timed optimization remains **best found**, not a proof of optimality. Batched search has a different improvement trajectory from sequential search; more candidates per second does not establish a better final deck.

## Measurements on this machine

The heavy multi-fusion benchmark selected the current four/eight Auto policy: four search workers assessed 1,126 candidates/s with 1600 sampled hands, versus 990 with two. Eight were slower for search (1,003/s), but reduced exact verification from 692 ms with four to 513 ms, with all 658,008 hands and routes identical. See `benchmarks/CPU_HEAVY_FUSION_RESULTS.md` and `benchmarks/cpu-heavy-fusions.json`. These measurements preceded the default-setting change and used the same analyzer and search algorithms.

### Earlier implementation baseline

The final Release benchmark used the real catalog and synthetic inventories, twelve logical processors, fresh analyzer instances with identical aggregate cache limits, three cold exact-analysis measurements per configuration, and two five-second searches per configuration. Benchmarks ran separately from the test suite. Raw evidence: `benchmarks/cpu-parallelism-final.json`; harness: `tools/YfmCompanion.OptimizerBenchmarks/ParallelismBenchmark.cs`.

| Workload | Serial | Selected parallel configuration | Observed gain |
| --- | --- | --- | --- |
| Exact diverse 40-card deck, median | 731 ms | 274 ms, four workers | about 2.7 times faster |
| Exact duplicate-heavy 40-card deck, median | 26 ms | 9 ms, four workers | about 2.7 times faster |
| Fusion-only search, mean completed candidates per five seconds | 67,641 | 77,680, 2 workers | about 15 percent more candidates |
| Campaign search, mean completed candidates per five seconds | 18,304 | 28,616, 4 workers | about 56 percent more candidates |

Every measured exact report matched the serial report's serialized SHA-256 hash, including all 658,008 physical hands, metrics, result ordering, and representative routes. These are fixture-specific measurements, not a universal speedup or live-gameplay responsiveness guarantee. Two workers and four workers did not scale equally on lightweight search. The current Auto policy selects four search workers for the measured heavier workloads. Preliminary larger-batch experiments did not improve lightweight throughput and were rejected.

Reproduce from the repository root:

```powershell
dotnet run --project tools/YfmCompanion.OptimizerBenchmarks -c Release -- . docs/benchmarks/cpu-parallelism-final.json --parallel
dotnet test YfmFusionCompanion.sln -c Release
```

The change only computes recommendations. Game memory, saves, ROMs and RetroArch configuration remain read-only.

The current guide-informed scoring adds separately labelled field/equip setup and removal metrics. See `research/GUIDE_SCORING.md`; old timing evidence predates this extra work. The four/eight Auto policy remains unchanged.

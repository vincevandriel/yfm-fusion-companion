# Heavy multi-fusion worker benchmark

2026-09-26, Ryzen 5 5600X (six physical cores, 12 logical processors). Uses the same isolated source snapshot as the six/eight-worker test, with the manual cap raised to eight and Auto capped at four. No installed application, game, save or production source was modified.

The real catalog resolver already explores sequential fusion routes using up to all five cards. This fixture ranks non-Exodia monsters below 1400 ATK by their number of legal fusion partners among those monsters, selects the top 80 card types, and grants three copies per type. Glitches are excluded. The exact fixture uses one copy each of the top 40 types.

A separate deterministic census sampled 2,000 five-card hands without replacement from each physical card pool, using the analyzer's initial-pair ordering and all subsequent ordered chains. In the chain-rich inventory, 58.7% of sampled hands supported at least one three-fusion/four-material route, and 18.25% supported a four-fusion/five-material route. The prior first-80 inventory gave 9.2% and 1.15%, respectively. Successful fusion-edge visits across all depths were about 6.1 times greater in the new inventory. These are fixture census statistics, not the user's deck probabilities. The exact 40-card fixture likewise supported three/four-fusion routes in 53.75%/17.3% of census hands.

Three trials per count, rotating count order and collecting garbage before each trial. Each analyzer/job was fresh; the aggregate retained-cache budget was 256 MiB. Quick search ran for five seconds. Higher sample counts increase candidate assessment work; they do not unlock deeper routes, which are already explored. Rates are means; exact times are medians. CPU is process CPU normalized across all 12 logical processors, including a small construction cost.

| Workers | Exact ms | 160 hands: candidates/s | CPU % | 1600 hands: candidates/s | CPU % | Campaign 1600: candidates/s | CPU % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2 | 1084 | 11,304.2 | 11.3 | 989.5 | 13.5 | 726.1 | 12.9 |
| 4 | 692 | 11,307.8 | 13.9 | 1,125.6 | 18.7 | 779.3 | 17.8 |
| 6 | 577 | 10,624.1 | 16.0 | 1,053.8 | 22.1 | 722.1 | 20.5 |
| 8 | 513 | 10,161.2 | 17.6 | 1,002.7 | 24.0 | 696.9 | 22.8 |

For fusion search with 1600 sampled hands, four workers improve throughput by 13.8% over two. Six are 6.4% slower than four, and eight are 10.9% slower. With campaign scoring, four beat two by 7.3%, six are 7.3% slower than four, and eight are 10.6% slower. At 160 sampled hands, two and four perform essentially equally. Eight raise measured CPU use without improving search throughput. Batch coordination, worker-local cache partitioning, candidate generation and memory traffic remain possible limits; this benchmark does not individually attribute the bottleneck.

Exact analysis scales better: six reduce elapsed time by 16.6% versus four; eight reduce it by 25.9%. All 15 serialized exact reports matched the one-worker SHA-256 baseline, including all 658,008 hands and representative routes. All 36 searches completed with a 40-card best-found deck and stayed within the cache budget; none claimed exact search results or optimality proof. A separate check confirmed the analyzer honored 1600 samples. All 126 original source-snapshot hashes remained unchanged. The earlier 16 focused regression tests also cover six/eight-worker cancellation, cache equivalence and recovery; no engine changes were needed for this experiment. Gameplay responsiveness was not measured.

For this workload, select manual four workers in the current application. Eight are promising specifically for exact final verification; the current executable still offers at most four. The timings do not justify raising every search phase to eight.

Raw measurements and inventory IDs/names: `cpu-heavy-fusions.json`. Corrected route census: `cpu-heavy-fusions-census.json`. The route census was rerun after the timing run to draw physical cards without replacement and exactly match initial-pair symmetry handling; timing fixtures were unchanged. Experimental harness: `../../artifacts/cpu-six-eight-source/tools/YfmCompanion.OptimizerBenchmarks/HeavyFusionBenchmark.cs`.

Reproduce:

```powershell
dotnet build artifacts/cpu-six-eight-source/tools/YfmCompanion.OptimizerBenchmarks -c Release -m:1
dotnet run --no-build --project artifacts/cpu-six-eight-source/tools/YfmCompanion.OptimizerBenchmarks -c Release -- artifacts/cpu-six-eight-source docs/benchmarks/cpu-heavy-fusions.json --heavy
```

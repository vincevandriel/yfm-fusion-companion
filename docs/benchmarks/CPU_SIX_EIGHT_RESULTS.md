# Six- and eight-worker benchmark

2026-09-26. Ryzen 5 5600X, 6 physical cores / 12 logical processors. The production application and its current Auto defaults were not changed. Tests used an isolated snapshot of the current source, with only the manual worker cap raised to eight; Auto remained capped at four.

Three trials per worker count and fixture, rotating worker order between rounds, with garbage collection before each trial. The aggregate retained-cache budget stayed at 256 MiB. Search trials used the same 80-card synthetic inventory, 160 sampled hands per candidate and a five-second Quick budget. CPU below is the benchmark process average normalized across 12 logical processors, not total system CPU; it includes a small job-construction cost.

| Workers | Diverse exact median (ms) | Fusion candidates/s (mean) | Fusion CPU (mean) | Campaign candidates/s (mean) | Campaign CPU (mean) |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2 | 389 | 15,862 | 11.0% | 5,092 | 12.3% |
| 4 | 241 | 14,060 | 13.4% | 5,898 | 16.4% |
| 6 | 192 | 13,301 | 14.9% | 5,894 | 18.1% |
| 8 | 172 | 12,738 | 16.4% | 5,529 | 21.3% |

Six workers reduced diverse exact-verification time by 20.2% versus four; eight reduced it by 28.5%.

Two workers remained fastest for lightweight fusion search. Four and six performed similarly for campaign search, while eight was slower. Raising worker count raised CPU consumption without consistently increasing search throughput. Eight is worthwhile for diverse exact verification on these fixtures; the current two-worker fusion and four-worker campaign search defaults remain justified.

All 30 exact reports matched their serial fixture SHA-256 hashes, including 658,008 hands and representative routes. 16 focused regression tests passed, including six/eight-worker cache equivalence, cancellation, recovery, progress, and higher-worker job lifecycle checks. All search runs completed with a legal 40-card best-found deck and stayed within the cache budget. More candidates does not prove a better final deck. Real gameplay responsiveness was not instrumented; no game, save, emulator configuration or installed application was modified.

Evidence: `cpu-six-eight.json`, `../../artifacts/cpu-six-eight-tests/six-eight.trx`, and isolated original-source hashes `../../artifacts/cpu-six-eight-source/source-snapshot-hashes.json`.

Re-run the isolated experiment:

```powershell
dotnet run --project artifacts/cpu-six-eight-source/tools/YfmCompanion.OptimizerBenchmarks -c Release -- artifacts/cpu-six-eight-source docs/benchmarks/cpu-six-eight.json --parallel
```

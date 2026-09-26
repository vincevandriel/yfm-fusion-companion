# Phase 3 gate — PASS, 2026-09-26

The previously outstanding changing-board integration is now verified during the user's normal unpaused Forbidden Memories gameplay. This gate accepts the optimizer/UI update for Phase 4 documentation and packaging. It does not claim a new release has been built, installed, pushed, or published.

## Evidence

| Check | Result | Artifact |
| --- | --- | --- |
| Balanced optimizer with real live polling | 36 unpaused duel reads, eight board changes; 63 successful reads, four safely rejected inconsistent reads; maximum concurrent reads one | `../benchmarks/phase3-playing-balanced.json` |
| Search responsiveness and result | First progress 70.8 ms; first preview 148.4 ms; max progress gap 222.4 ms; exact 658,008 hands; NOT proven optimal | Same benchmark |
| Real production-WPF presentation | 53 successful reads, 51 unpaused; 15 changes; 52 selection/scroll checks including all 15 changed snapshots; nonzero scroll offset 27 | `phase3-playing-ui-repaired/real-live-presentation.json` |
| Mid-read transition and recovery | Seven transient rejections preserved collection selection/scroll; unvalidated hand/advice withheld; zero failures | Same UI artifact |
| Final transient label, recovery and disconnect regression | Passed in production WPF using synthetic data | `phase3-playing-regression/live-interaction.json` |
| Engine regression suite | 200 passed, zero failures/skips | Local `tests/YfmCompanion.Tests/TestResults/phase3-playing-repair.trx` |
| Final desktop/audit build | Zero warnings/errors with `-warnaserror` | Build command below |

Earlier independent correctness, proof, source-state, desktop lifecycle, cache, preparation and visual evidence is indexed in `PHASE3_AUDIT.md`. This continuation changed transient presentation and audit tooling only; engine algorithms remain unchanged. The real UI run preceded a final status-text/color-only addition, which was exercised by the targeted WPF regression afterward.

## Finding resolved

Mid-read changes are expected during real gameplay. The reader intentionally withholds an inconsistent hand/field snapshot. Previously the UI cleared the constructed deck and collection too, destroying selection/scroll. It now retains only the last validated collection/deck with explicit status, clears dynamic cards and advice, then refreshes normally. Actual disconnect, wrong game and protocol failures still clear all data. Validation was not relaxed and no unsafe snapshot was accepted.

The original benchmark's `FailedReads=4` consists exclusively of `RetroArchTransientStateException`; preserve that field and the artifact rather than relabeling it as zero. The initial UI audit recorded seven such rejections as failures before transient-state handling was added to the audit. That initial artifact is preserved at `phase3-playing-ui/real-live-presentation.json`.

## Reproduction

From the repository root, with the player voluntarily running an ordinary unpaused duel:

```powershell
dotnet run --project tools/YfmCompanion.OptimizerBenchmarks -c Release -- . docs/benchmarks/local-playing.json --live
dotnet run --project tools/YfmCompanion.UiAudit -c Release -- docs/audit/local-playing --real-live
```

Run the two live commands sequentially. The UI harness uses isolated temporary settings, disables background services, and performs sequential read-only polling for 60 seconds through the production reader and presentation. It records counters/timing only; no personal card IDs, memory bytes, save data, paths or gameplay screenshots are written. It never sends game controls or writes game/save/config data. Normal gameplay must generate at least two observed changes and ten unpaused reads; otherwise this gate fails.

```powershell
dotnet test YfmFusionCompanion.sln -c Release --no-restore
dotnet build tools/YfmCompanion.UiAudit -c Release --no-restore -warnaserror
dotnet run --project tools/YfmCompanion.UiAudit -c Release --no-build -- docs/audit/local-transition --live-interaction
```

## Retained limits and handoff

- The recorded visual matrix uses logical/scaled renders; native Windows DPI changes were not tested. Carry this limitation into release documentation.
- Live evidence covers this machine/core/game and observed normal gameplay, not every duel state or region. Synthetic regression complements the observed transitions.
- The real UI audit invokes the production reader and presentation/transient paths with background services disabled; optimizer coexistence is measured separately. Full desktop lifecycle/polling guards have prior synthetic evidence.
- Timings are run-specific; tests ran alongside the repaired UI observation. Cache accounting is not a hard process-memory cap.
- Pause at this phase boundary. Phase 4 is assigned to GPT-5.6 Luna, Medium reasoning, in `OPTIMIZER_UPDATE_PLAN.md`.

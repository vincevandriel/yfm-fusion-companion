# Optimizer update checkpoint

## Status and resume instruction

- **Phase 1 implementation gate PASSED on 2026-09-24. Paused before Phase 2.**
- Phase 1 implementation commit: `46c71e1c836c7b4b771143a0f1561dc0f3287d88`.
- Original base: `a967903cb076e3c444383bdbadb400abdc91fd66`. The checkpoint-only commit following the implementation commit does not change tested code.
- **Resume with GPT-5.6 Sol (`gpt-5.6-sol`), reasoning Medium.**
- Exact next action: read `OPTIMIZER_UPDATE_PLAN.md` and `docs/benchmarks/PHASE2_ENGINE_CONTRACT.md`, then begin Phase 2's shared collection/snapshot service and automatic-newest-validated-save workflow before wiring the owned gallery and redesigned views.
- Phases 2, 3 and 4 have **not** started. This gate is not the separate Phase 3 independent audit or a release approval.
- Local commits only. No push, publication, installed-release replacement, game controls, game-memory writes or save/config edits.

## Completed Phase 1 work

- Shared compact exact/sample hand evaluator; correct weighted physical-hand totals, integer total best-ATK numerator, explicit sample count/uncertainty and exactness labels. Scratch buffers are reused under the analyzer lock without mutating earlier reports.
- Versioned transitive comparator shared by searches, purchase plans and proof. Fixed 0.5-percentage-point / 25-ATK groups, exact rational hand metrics, modeled safety, gauntlet tie-break, lower spending and deterministic numeric card-ID ordering.
- Request-scoped prepared card, pair, terrain and opponent assessments, including password-purchase scoring. Combined accounted cache budget is 256 MiB (224 MiB hand/deck + 32 MiB assessments). This is not a cap on process RAM or cumulative allocations.
- Removed redundant purchase re-optimization; comparison deck analyzed once after purchase alternatives. Rejected timed/proof candidates no longer need full inclusion explanations. Published results remain immutable, described reports.
- Frozen background Quick / Balanced / Thorough jobs with 5 / 60 / 900-second search budgets, early candidate, incremental improvements, up to 2 / 8 exact finalists, compatible verified-incumbent preservation, VerifyBest, Pause/Resume and Stop-and-keep-best.
- Stage-specific counts, elapsed/search time, credible verification ETA range, structured arbitrary-precision proof progress, time-throttled updates and cancellation within expensive loops. UI must install the result before displaying Ready.
- Full capacity-vector proof without a heuristic shortlist; budget, eligibility, prior-redemption exclusions, copy limits and one-purchase-per-name checks. Only full enumeration can report proven optimal for the frozen model.
- Proof preflight capacity count and explicitly qualified measured work extrapolation. Schema-2 atomic/checksummed checkpoints, exclusive lease, input/catalog/objective/rules identity checks, initial-preparation pause, incomplete-leaf replay, and durable preservation of a carried-in verified incumbent.
- Approved update plan, engine/UI handoff, before/after benchmarks, raw evidence and machine-readable Phase 1 gate record.

## Verification

- **190/190 tests pass**, zero failures. Local result: `tests/YfmCompanion.Tests/TestResults/phase1-final.trx` (ignored generated output).
- **13/13 legacy regression probes pass**. Local evidence: `tmp/optimizer-update-legacy-audit.json`. Its historical phase/baseline/release labels do not refer to this update's Phase 4.
- Full solution Release build passed with **0 warnings and 0 errors** using `--artifacts-path tmp/phase1-isolated-build`.
- Normal-output build initially failed because the already running companion held files under `tools/YfmCompanion.UiRender/bin`. It was not killed. The isolated build verified all solution projects without overwriting that active copy.
- Formatting verification and Git whitespace checks pass.
- Independent physical-hand and quantity-vector oracles, 105-deck proof enumeration, purchase-budget proof winners, 658008 accounting, comparator transitivity, cached/uncached equivalence, scratch reset, frozen inputs, rejected incompatible/corrupt checkpoints, immediate pause, partial-leaf resume and incumbent restart recovery all pass.
- Final synthetic 48-case matrix: **42 complete / 6 clean diagnostic cancellations**, versus baseline **14 / 34**. Two-second cutoff; maximum final progress gap **258 ms**, maximum cancellation overrun **21 ms**. Evidence: `docs/benchmarks/optimizer-gate-matrix.json`.
- Latest 60-second Balanced/live run: **68 successful active-duel reads**, no errors, no overlap; first progress **46.6 ms**, first legal deck **994 ms**, max progress gap **752 ms**, two exact finalists and **658008** hands. Evidence: `optimizer-reuse-live-balanced.json`.
- Full 900-second Thorough run: **908 successful active-duel reads**, no errors/overlap, eight verification starts and an exact 658008-hand winner. It predates the final cache/report refinements; those are covered by the later Balanced run, gate matrix and final regressions. Evidence: `optimizer-live-thorough.json`.
- Profiled Balanced allocation decreased from **33.77 GB to 11.43 GB cumulatively**, while candidates examined increased from **145172 to 404580**. This is allocation volume across a minute, not RAM retained. Latest recorded process peak ~213.1 MiB and accounted cache ~228.2 MiB / 256 MiB. Cold diverse-hand allocation remains higher than pristine baseline; do not promise universal speedups or zero memory tradeoffs.
- Read `docs/benchmarks/README.md` for reproducible commands, all intermediate measurements and limitations; `docs/benchmarks/phase1-gate.json` records the gate and audited source hashes.

## Remaining work / boundaries

- **No known critical Phase 1 gate failure remains.** Independent review still belongs to Phase 3 and may return defects to this phase.
- Phase 2 must integrate the new job API and actual progress panel, shared newest-save discovery, owned-only virtualized gallery, artwork fallback/cache, manual collection workflows and all-tab redesign.
- Preserve Compact Live width/unrestricted resizing, result name, ATK, numeric / F(X) routes and compact guardian advice. Do not add material-name routes or galleries there.
- Timed pause is in-memory on the same job instance; only proof checkpoints support restart across application exits. Explain this explicitly.
- Estimates, exact statistics and global proof are different. Modeled opponent safety is not a duel-win probability. Large proof spaces can be impractical.
- No WPF redesign screenshots, multi-DPI review, installed UI progress test, release packaging or public-download validation is claimed by this engine gate.
- The independent Phase 3 review must use GPT-6 Astra xhigh; release Phase 4 must use GPT-5.6 Luna Medium after that audit passes.

## Process and workspace state

- No agent-started optimizer job, proof search, benchmark or live polling loop remains running. Test proof checkpoints were synthetic and cleaned up by the tests.
- Existing companion/RetroArch windows were not closed or controlled. The user's newly appearing `.vs/` directory is left untouched and untracked, not included in the implementation commit.
- No new subagents were started. Earlier delegated tasks had been interrupted before editing.
- Usage-limit telemetry is unavailable through the exposed tools; no quota check is claimed.

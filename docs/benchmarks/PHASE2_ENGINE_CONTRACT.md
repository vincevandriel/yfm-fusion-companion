# Phase 2 engine integration contract

This is a handoff for the approved native WPF redesign, not permission to begin Phase 2. Read the current checkpoint for gate status. Keep the installed release separate from development builds.

## Entry points and frozen inputs

- Create a `DeckBuildRequest` from a **validated ownership snapshot**, not Library sightings. It contains quantities, scoring options, mode, Star Chip permission/budget and a source identity. Include the save content hash in that identity; do not use a path alone.
- For a campaign goal, prepare `SafetyContext` and any final-gauntlet `SecondarySafetyContext` with `CampaignOptimizationContextBuilder`. Reuse a builder for the same catalog/research snapshot. Forward its progress and cancellation while preparing; do not do this work on the UI thread. Apply the selected terrain to both contexts as the existing campaign facade does.
- `DeckBuildJob` deep-freezes the request before starting. A new save or edited controls must not change the active job. Show pending source changes separately.
- Use `Quick`, `Balanced`, or `Thorough`; their **search** budgets are 5, 60 and 900 seconds. Preparation and exact verification are separate costs. `MaximumWorkerCount` is a ceiling; the current job intentionally uses one computation worker.
- The existing synchronous planner remains for compatibility; do not accidentally invoke both it and the new job for one button press.

## Progress and control

- Keep one job instance for timed Pause/Resume. `RunAsync` starts a new job or resumes a paused instance. `Pause` and `StopAndKeepBest` are nonblocking requests; await the active task before enabling restart or replacing the job.
- Timed mode pause state survives in that instance, **not an application restart**. Durable restart/resume belongs to proof mode. Explain this distinction in the UI and exit prompt.
- `DeckBuildProgress` provides stage/state, elapsed time, search time consumed/budget, candidate count, best completed candidate, an estimated challenger, exact/sample hand counters and a measured verification ETA range when meaningful. This 0.5x–2x extrapolation is not a confidence interval. Proof has separate `BigInteger` resolved/total-space and legal-deck counts; its total runtime is not inferred from a partially evaluated leaf.
- Marshal notifications to the dispatcher without blocking computation. Guard callbacks with a job generation ID so delayed callbacks from an old job cannot overwrite the current result. Preserve notification order.
- Use the search budget as a time bar labelled **Search budget**, not a claim that the whole job is that fraction complete. Use hand counters for analysis/verification. Unknown work has activity/counts, not an invented percentage.
- The worker's `Completed` / “Result ready for installation” means the result is available. Set the screen's **Ready** state only after installing that result in the view.
- Inspect `Report.ExactAnalysis.IsExact`: the property retains its legacy name even for sampled reports. Show sample count and uncertainty only when false. Never infer exactness from that property name or from 100% on a sample-stage bar.
- `ProbabilityMargin95` is an approximate worst-case per-percentage sampling margin, not a guarantee for a deck selected by searching many estimates. Exact verification is the remedy for search selection bias.
- `Best` may deliberately remain a verified incumbent while `EstimatedChallenger` changes. Do not compare their displayed rounded percentages to override the shared comparator.
- `VerifyBestAsync` exactly evaluates a completed/stopped timed job's candidate; it does not prove global optimality. `AdoptVerifiedIncumbent` accepts a compatible prior exact result and checks input/catalog/scoring identity and legal spending.
- A failed task can still have a last completed best candidate in its last progress event. Display the failure and retain that candidate without calling the failed job completed.

## Advanced proof mode

- Call `DeckProofPreflight.PreviewAsync` before confirmation. Display the full arbitrary-precision capacity-vector count. If available, label the measured range **unpruned work extrapolation**; it is not an exact feasible-deck count or confidence interval.
- Ask for or select an application-owned checkpoint path. Do not put personal proof inputs/checkpoints in the repository, release package or public diagnostics.
- `DeckProofSearch` and the `DeckBuildProgress.Proof*` fields expose structured `BigInteger` resolved-space and legal-deck counts. Its progress is **search space resolved**, since budget-invalid branches can be resolved without evaluating a deck. The job wrapper also provides readable proof-stage text; do not parse that text to recover numbers. The ordinary timed-mode candidate counter is not a proof-space counter.
- Proof cancellation durably checkpoints complete work. An interrupted leaf is replayed on resume. Checkpoints are checksummed, versioned, atomically replaced and exclusively leased while running. The `.lock` file's existence alone is not a running job; the exclusive open handle is the lease.
- Current checkpoint schema is 2. Incompatible rules, objective, catalog, schema or frozen inputs must produce the supplied error and a choice to start a **different** checkpoint. Never silently overwrite/reinterpret an incompatible one.
- Only `ProvenOptimal == true` permits **Proven optimal for the selected model and inputs**. Exact opening-hand statistics and completed timed searches do not.
- No legal deck: explain insufficient legal ownership/purchases. Complete enumeration of an infeasible space does not produce an optimal deck.

## Ownership, purchases and presentation

- A proposed purchase is the deficit between chosen copies and the frozen owned quantity. Derive per-card purchase counts from those inputs, check their sum against `RequiredStarChips`, and show passwords/costs from the catalog. The engine does not buy cards or alter saves.
- Prior-redemption exclusions are user declarations: a save does not establish password redemption history. Copy limits, eligible passwords, one purchase per name and budget are enforced by the common quantity space.
- Modeled opponent coverage/counter scores are **not measured duel-win probabilities**. Unknown opponent guardian selection remains conservative; do not present it as observed AI behavior.
- The retained search cache has a 256 MiB accounted-object budget; process memory and total allocation volume are separate metrics. Phase 2 must independently implement the 64 MiB thumbnail budget.
- Rejected timed-search candidates use lightweight score reports internally. Published best candidates and final results have descriptions restored; do not expose the internal evaluation method as a player-facing report API. A result's `SearchCache` snapshot reports aggregate hand/assessment-cache accounting for timed jobs; a null value in proof results means telemetry is not supplied, not zero memory use.
- Preserve canonical fusion resolution, field-first ordering and terminal equips. Do not expand Compact Live: keep result name, ATK, numeric / `F(X)` routes and compact guardian lines, unrestricted resizing, and no material-name/gallery controls.

## Phase 2 validation still required

No engine benchmark is a WPF acceptance test. Verify the real progress panel, keyboard controls, cancellation, result installation, all source transitions, gallery virtualization, artwork cache, all five tabs, Compact Live and the specified display sizes/scales. Run the one-second live reader concurrently. Phase 3 remains a separate independent audit.

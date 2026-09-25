# Phase 3 independent audit

Started 2026-09-25 from `3705fde977224a7529365c4699499d5358374213`.

Status: **in progress; release gate NOT passed**. Earlier Phase 2 acceptance claims are reopened by the findings below.

## Audit sequence

1. Engine correctness: preview/resume state, exact proof, comparator, purchase legality, cancellation, cache bounds and frozen results.
2. Save and desktop integration: asynchronous source transitions, stale data, manual edits, progress, job ownership, artwork, keyboard and layout.
3. Independent regression/performance/live checks and a visual state matrix, followed by a documented pass/fail decision.

## Findings at source review

- E01: the early preview has zero evaluated hands but is inserted into the scored shortlist. Pausing at that notification and resuming bypasses its initial evaluation. An unexplored preview can be compared as though its zero scores were measured. The first evaluation also sits outside the search budget timer.
- E02: optimizer construction performs two uncancellable catalog-wide heuristic scans. Cold pause/stop behavior is not covered by the prior warm second-run UI timing.
- E03: the UI can display fabricated zero probabilities, modeled-safety values and a Verify check mark for an unevaluated/stopped preview or Quick result.
- S01: an async refresh applies its result even if the player switched to Manual or pinned another file while awaiting the read. Pending save data is also applied on pause and can overwrite manual edits.
- S02: the unchanged-content shortcut drops stale flags, saved-time changes and corrupt-newer warnings. A now-missing source can remain labelled up to date.
- S03: inspection and hashing read the file separately, so the frozen identity can describe bytes different from the parsed collection. Discovery synchronously performs disk/process scans before its first await.
- U01: preparing campaign threats and proof preflight run before the optimizer's busy/cancel guard. Multiple starts and edits can race, and preparation lacks its required progress/cancellation integration.
- U02: completion formatting uses current editable quantities/budget rather than the frozen request; a newer save can clear the just-produced result. Pause/verification and delayed progress need lifecycle guards.
- U03: the gallery clamps chest-plus-deck ownership to 99, does not notify chest/deck distribution-only changes, and replaces ItemsSource on every update, losing selection/scroll. Artwork is eagerly held on all card rows, defeating LRU eviction; oversize entries may exceed the declared thumbnail cap.
- U04: the prior screenshot set has clipped headers, white/low-contrast disabled controls and a horizontally forced layout at high DPI. Several tabs remain tables/pickers rather than the agreed visual trays/slots. One optimizer focus traversal does not establish all-tab keyboard acceptance.
- T01: the UI audit overwrites real settings, restores them only on success, uses a fixed temporary directory, and has no assertions that most timing/state targets passed. The audit must be isolated and fail closed.

Findings will be marked repaired only after targeted regression evidence. Phase 3 cannot pass until all critical correctness and usability findings are resolved.

## First repair checkpoint — 2026-09-25

- E01 repaired: previews have no invented outcomes/safety values and are excluded from scored finalists. Resume scores the preview before any challenger comparison. Initial scoring uses the timed search budget. A new pause/resume regression passes.
- E02 repaired at source: advanced card details are precomputed once per immutable catalog; heuristic preparation is lazy and cancellable. Further benchmark coverage remains required.
- S01/S02 repaired for tested transitions: refresh requests are serialized, superseded responses are rejected, manual changes invalidate pending reads, paused jobs retain pending saves, and unchanged card bytes still update freshness/warnings. The UI audit asserts missing → stale → recovered and an in-flight read → protected manual mode.
- S03 repaired: stable reads compare repeated bytes/metadata and hash the exact parsed bytes. Discovery runs off the dispatcher and supports combined core/content subfolders. A torn-save recovery/hash test and deterministic nested-discovery test pass.
- U01/U02 partially repaired: busy/cancel is active during campaign preparation and proof preview; UI presentation uses frozen owned quantities and Star Chips; old progress callbacks are rejected after result installation. A legal unscored deck can appear before campaign preparation completes. Paused jobs can be stopped. Quick completion no longer displays a Verify check mark; zero-hand metrics show dashes. Standalone verification's terminal UI state and broader race coverage still need review.
- U03 partially repaired: gallery has a finite viewport enabling virtualization, stable ordered items are retained across progress notifications, chest/deck-only changes notify bindings, raw ownership is no longer clamped to 99, and rows load artwork lazily without retaining all decoded images. Oversize thumbnails are not retained beyond the configured limit. Replaced-art invalidation and measured thumbnail accounting still need dedicated coverage.
- T01 partially repaired: UI audit uses a unique temporary settings directory through `YFM_COMPANION_SETTINGS_DIRECTORY` and cleans it in finally; personal settings are never rewritten. Timings and real paused/cancelled states are now asserted. All-tab keyboard coverage and error-state screenshots remain open.

Validation after these changes:

- Full suite: **197 passed, 0 failed, 0 skipped**.
- Full solution build with `-warnaserror`: **0 warnings / 0 errors**.
- `dotnet format` completed successfully; Git whitespace check passed.
- Isolated synthetic UI smoke: busy **11.0 ms**, first legal deck **1,009.9 ms**, pause **138.5 ms**, stop **101.7 ms**; actual job pause/resume/cancel assertions and source-transition assertions passed.
- Machine-readable smoke evidence: `phase3-ui-smoke.json`. Generated screenshots remain local under `tmp/phase3-ui/`; they are not an all-tab visual acceptance pass.

## Still required before the gate

- Complete E03/U02 lifecycle review: standalone Verify completion, preparation pause semantics, freezing selected inputs on explicit source changes, proposed-copy reset, and proof checkpoint recovery from the desktop.
- Check comparator grouping of campaign opening-answer coverage against the fixed 0.5-percentage-point contract, canonical proof comparisons and compatible-incumbent reduction. No comparator change was made during this checkpoint.
- Correct and independently test the remaining visual redesign gaps (U04), all-tab keyboard navigation, narrow/high-DPI controls, selected/disabled contrast, true view separation and required visual trays/slots. The historical Phase 2 screenshot claim is not accepted as sufficient.
- Finish gallery/artwork cache and stable selection/scroll tests, manual ownership persistence/labels and source-directory watcher edge cases.
- Rerun measured before/after matrix, cold preparation/progress/cancellation, exact hand/proof independent oracles, caches and one-second live coexistence. RetroArch process 29256 was present during this checkpoint; this audit has not yet established active-duel coverage for the repaired build.
- No release, public upload, installed replacement or Phase 4 work is authorized by a passing smoke test alone.

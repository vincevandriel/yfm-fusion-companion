# Phase 3 independent audit

## Fourth repair checkpoint — 2026-09-26

**Automated desktop/engine checks pass; Phase 3 remains open for live changing-board sign-off. Phase 4 has not started.**

- A shared optimizer activity view model now drives inline progress and a fixed bottom strip. Active/paused progress stays visible while scrolling or changing tabs, and remains absent from Compact Live. Gallery browsing/filtering stays enabled; quantity edits and request settings remain locked.
- Sample completion no longer fills the timed-search bar. The bar represents search-budget consumption during searching and physical hand counts during verification. Elapsed time is formatted compactly. A regression checks 160/160 sampled hands at one second of a five-second search gives 20%, not 99%.
- Pausing during preparation preserves frozen source/settings and resumable controls; stopping releases them. The full native proof workflow now runs through Build/preflight/confirmation/cancel/start/pause/close/reopen/resume/proven installation and explicit restart with byte-for-byte preservation of the previous checkpoint.
- Full-mode metrics reflow, table text wraps, numeric columns have explicit minimum widths, and horizontal scrolling stays accessible within the available viewport. Narrow headers use less vertical space. The proof dialog scrolls within its owner's available height. Audit confirmation windows do not activate over the user's game.
- Twenty changing synthetic snapshots passed selection/scroll preservation through the actual Live Duel presentation; disconnect cleared stale rows. This is synthetic integration, not evidence of a changing real duel.
- Latest evidence: `phase3-final-desktop.json`, `phase3-final-visual-states.json`, `../benchmarks/phase3-preparation.json`, `../benchmarks/phase3-active-duel-balanced.json`. Selected synthetic captures are committed in `visual-samples/`; all 455 captures remain under ignored `tmp/phase3-final-visual/`.
- Desktop timings: busy about 10 ms, first legal deck 1,010 ms, pause 58 ms, stop 126 ms; 152 keyboard focus visits. Complete proof, preparation pause, fixed-dock, locked-edit and synthetic scroll tests passed. The artifact's final failed label is the intentionally incompatible-request test.
- Six cold/warm preparation cases passed. General campaign: 60.25 ms cold / 0.89 ms warm, maximum gap 17.09 ms; cancellation delay 1.19 ms. Timings are reference-machine observations, not universal guarantees.
- Balanced/live coexistence: 67 successful active-duel reads, zero failures, maximum concurrent reads one, first legal preview 105.13 ms, max progress gap 224.84 ms; exact 658,008 hands and `ProvenOptimal=false`. **PlayingReads=0, DuelStateChanges=0**. RetroArch was paused throughout. Concurrent visual work was present, so this is not an isolated throughput benchmark.
- Visual evidence: 150 tab/state/resolution/scale cases, top/middle/detail captures, dropdown and four Compact captures; representative adverse layouts reviewed. Zero automated text warnings alone is not acceptance. Host DPI was 100%; 125/150/200% were logical/scaled renders. Actual Windows DPI changes were not performed.
- Full solution tests: 200 passed, none failed/skipped. Builds and formatting checks succeeded. One audit attempt failed because its own running screenshot process locked the executable; sequential reruns resolved that tooling issue. A new test's visual-parent lookup was corrected to select the logical nested tab; subsequent interaction tests passed.

**Remaining sign-off:** read an unpaused real duel while the player plays normally, record actual state changes and verify live presentation/scroll under them. No special moves, game inputs or save writes by the companion are needed. Retain the actual-OS-DPI coverage limitation in release documentation; do not claim it tested. Do not enter Phase 4 until the Phase 3 gate decision is recorded.

## Third repair checkpoint — 2026-09-25 (gate remains open)

- Extracted all five tab layouts into tab-specific views with forwarded commands and a shared application workflow. Added a grouped Deck Analyzer tray, add/remove-copy controls, and selected-row route/intermediate details. No duplicate catalog or connector was introduced.
- Separated the pinned source from the last successfully loaded file. Missing/recovered files and racing pin selections are tested. Same-content saves refresh timestamps without resetting collection identity. Empty saved decks clear obsolete analyzer inputs instead of claiming 40 cards loaded; chest ownership is retained.
- Closing now cancels background work, waits for tracked activities and durable proof checkpoint completion, then disposes services. Automated close-during-proof/restart, real folder watcher updates/deletion/recovery, native proof confirmation approval/cancellation, copy-count/add/remove/40-slot tray tests pass. The confirmation window and proof job are exercised independently; the entire preflight/dialog/restart click sequence remains to review.
- Offline UI audits disable live networking and automatic source changes. All fixtures, artwork and settings are synthetic and isolated; temporary fixture directories are cleaned on exit.
- Visual review found and fixed pale selected DataGrid cells behind white text. Compact columns now fit a 260-DIP window without changing window limits or adding material names. Selected route details remain readable, but narrow table headers/results still truncate and need further UX review.
- `phase3-desktop-lifecycle.json`: busy 10.26 ms, first deck 897.11 ms, pause 162.57 ms, stop 176.35 ms, 152 forward/backward keyboard focus visits. Its terminal failure label is the intentionally incompatible proof request.
- `phase3-visual-states.json`: 150 cases / 304 images covering five states, five tabs, 1366x768 and 1920x1080 at 125/150/200%, plus Compact. Images remain in ignored `tmp/phase3-states-final/`. This is logical WPF rendering, not actual OS-DPI switching. Zero automated unwrapped-text warnings does NOT establish visual acceptance: the detector excludes table cells and misses some offscreen content. Representative images were reviewed, not every pixel of every capture.
- `../benchmarks/phase3-live-quick.json`: 12 successful active-duel reads, zero failures, maximum concurrency one. Playback was Paused throughout: changing-board behavior is unverified. Quick first preview 103.21 ms, maximum progress gap 221.71 ms, 160 sampled hands, NOT exact or proven optimal. Visual audit ran concurrently; timings are machine/run-specific.
- Full solution: 200 tests passed, no skips/failures; warning-as-error build clean. Solution and UI-audit formatting checks and whitespace check passed before the final fixture-only extension, which passed its rerun.

Remaining: inspect intermediate scroll positions/metric panels/dropdowns/disabled and error states, improve narrow table readability and progress visibility while browsing the collection, confirm final visual acceptance including real DPI where available, exercise complete proof-start/restart/preflight lifecycle, and test changing duel snapshots/scroll plus cold campaign preparation responsiveness. The main workflow still owns significant logic although presentation is separated. Phase 4 is not authorized by these partial gates.

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

The list below describes the first checkpoint's open work; consult the second repair checkpoint for current closures and remaining limits.

- Complete E03/U02 lifecycle review: standalone Verify completion, preparation pause semantics, freezing selected inputs on explicit source changes, proposed-copy reset, and proof checkpoint recovery from the desktop.
- Check comparator grouping of campaign opening-answer coverage against the fixed 0.5-percentage-point contract, canonical proof comparisons and compatible-incumbent reduction. No comparator change was made during this checkpoint.
- Correct and independently test the remaining visual redesign gaps (U04), all-tab keyboard navigation, narrow/high-DPI controls, selected/disabled contrast, true view separation and required visual trays/slots. The historical Phase 2 screenshot claim is not accepted as sufficient.
- Finish gallery/artwork cache and stable selection/scroll tests, manual ownership persistence/labels and source-directory watcher edge cases.
- Rerun measured before/after matrix, cold preparation/progress/cancellation, exact hand/proof independent oracles, caches and one-second live coexistence. RetroArch process 29256 was present during this checkpoint; this audit has not yet established active-duel coverage for the repaired build.
- No release, public upload, installed replacement or Phase 4 work is authorized by a passing smoke test alone.

## Second repair checkpoint — 2026-09-25

This remains an **in-progress audit, not release approval**.

- Standalone Verify now shares result installation, late-callback suppression and terminal controls with search. Engine tests cover verification pause/resume/stop and retained best-on-failure. Worker progress is capped below 100%; only installed results reach Ready. Unknown proof work shows activity, and timed progress explicitly labels search-budget consumption.
- Manual collections now persist across actual window restart, preserve quantities above 99, hide inherited chest/deck provenance, and discard obsolete proposals/Verify controls when quantities change. Pending saves are tested while a real job is paused and are applied after stop.
- Campaign opening-answer coverage now participates in fixed 0.005 primary groups before the gauntlet tie-break; exact primary metrics break ties afterward. Comparator version is `campaign-lexicographic-v3`; old proof objectives cannot resume silently. Boundary and transitivity tests pass.
- Artwork uses bounded LRU accounting and stream decoding, bypassing WPF's separate URI cache. Tests check replacement pixels, cache reuse/eviction, deletion, invalid images and oversize rejection. Explicit artwork refresh invalidates even unchanged metadata.
- The high-DPI header no longer overlaps, horizontal forcing was removed, strategy controls reflow, advanced options are collapsed, and progress moved above the gallery. Theme templates prevent native white/low-contrast disabled inputs and combo surfaces. Reusable adaptive columns, card tiles and detailed card slots are used in the full interface. Compact sizing and route notation are unchanged.
- Saved/live cards have shared visual tiles. Live lists reconcile changes without replacing ItemsSource; changed/deleted selected slots have regression coverage. Gallery auditing asserts fewer than 30 realized containers for roughly 100 owned cards and stable items/selection on unchanged refresh.
- Autocomplete no longer retains stale suggestions after a selection; clearing resets card details. Deck Analyzer now has a real progress bar and rejects callbacks/results from a deck edited during analysis.
- Desktop proof tests pause to disk, recreate the job, complete a one-feasible-deck proof, and reject changed inputs with failed-state controls. An explicit advanced restart option preserves an old checkpoint; an engine-compatible lease prevents archiving another active job's file. Timed pause labels explicitly require keeping the window open.
- UI audit now traverses all five tabs forward and backward, isolates its settings/save/artwork/proof files, and tests source, verification, manual restart, gallery, card-entry and proof workflows. The native proof confirmation dialog is not exercised by the direct job harness.

### Fresh performance evidence

`docs/benchmarks/phase3-matrix.json`: 42/48 completed under the two-second diagnostic cutoff, six clean cancellations, maximum progress gap 230.8194 ms, maximum cancellation overrun 22.0901 ms. The comparable pristine matrix completed 14/48 with 12,911 ms maximum gap and 10,912 ms overrun. This is the same synthetic harness, not a universal speedup claim. The diverse/manual/no-purchase cold and warm cases took 1,069.15 / 10.42 ms, allocating 124.37 / 0.96 MB; the historical baseline was 1,523 / 1,512 ms and about 17.8 MB each, so the cold-allocation tradeoff remains.

`docs/benchmarks/phase3-live-balanced.json`: 68 successful read-only one-second polls, zero failures, maximum concurrent reads one, first progress 46.79 ms, first legal preview 119.63 ms, maximum progress gap 222.66 ms. Balanced completed with exact 658,008-hand statistics and **ProvenOptimal=false**. Accounted search caches used 235,539,010 of 268,435,456 bytes; this is not a process-memory cap. **ActiveDuelReads=0**, so this is menu/non-duel coexistence, not active-duel acceptance.

### Remaining Phase 3 gate work

1. Complete independent visual state review for all tabs: empty/populated/loading/error/completed, every required resolution/scale, dropdowns, disabled/selected states and Compact Live. Current synthetic captures are rendered WPF DIP/scaling cases, not a real OS-DPI-switch test. Layout fixes and keyboard traversal alone do not close this gate.
2. Complete tab-specific presentation separation, deck-tray copy-count presentation and inspectable route/intermediate-result UX against the approved plan. Shared components now exist, but the main-window controller still owns substantial tab-specific logic.
3. Audit remaining watcher/pinned-source races, proof confirmation/restart controls and shutdown during active work. Job-level proof recovery is covered; the complete confirmation-and-close workflow still needs automation.
4. Measure live-card scroll preservation under actual changing duel snapshots and cold campaign preparation responsiveness across fixtures. A current active duel was unavailable in the measured run; do not substitute historical coverage.
5. Rerun affected regression/visual/performance gates after further repairs, then make an explicit Phase 3 pass/fail decision. **Do not begin Phase 4.**

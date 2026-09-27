# Windows 2.0.1 release audit

Offline release acceptance: **PASS** (2026-09-27). Public download and retirement receipts are recorded separately under [release records](releases/README.md); old binaries are retained until that final delivery check passes.

## Scope and acceptance

Audit the published 2.0 source, desktop lifecycle, data integrity, optimizer correctness, dependency health, executable resources, package layout and public delivery. Keep game/save/configuration access read-only. Archive small diagnostic records and preserve Git history; remove superseded binary packages only after the current package and its public download pass.

Required checks: locked restore; full build with warnings as errors; formatting; regression suite; known-vulnerability report; deterministic catalog rebuild; research invariants; independent acceptance probes; production WPF rendering; lifecycle/cancellation/recovery interactions; all artwork hashes and decoding; published executable self-check; embedded icon; normal startup; documentation links/source index; ZIP extraction/hash verification; GitHub CI and public download hash.

Native OS-DPI switching and new changing-board gameplay are distinct from synthetic rendering and offline/package checks. Record them separately if available; never turn unavailable runtime evidence into a passing claim.

## Findings and repairs

| Finding | Consequence | Repair |
|---|---|---|
| Multiple one-off publishers and a different GitHub packaging path | Local and public packages differ and accumulate redundant files | One versioned `tools/Publish-Release.ps1` used locally and by CI |
| UI audit indexed a fifth, removed Save Snapshot tab | Screenshots could show an empty selection while control-level checks passed | Four current tabs and named optimizer selection; bounded Compact assertions |
| UI audit, acceptance and benchmark projects omitted from the solution | A solution build could leave old audit binaries in use | All twelve projects included in solution restore/build |
| Whitespace gate had drifted since earlier features | Clean source verification failed | Apply formatter and verify again |
| Git normalized the SQL dump on a fresh checkout | CI rebuilt identical facts with a different recorded input hash | Preserve SQL source bytes with `-text`; keep exact database hash gate |
| No executable/window icon | Generic Windows executable appearance | Original editable SVG plus nine embedded ICO sizes |
| Flat runtime folders and benchmark files mixed into player package | Harder to identify what to launch and what to read | Executable/START-HERE with Resources, Documentation and Licenses |
| Startup-only test did not prove resources were usable | A surviving process could still show a database error | Explicit offline `--verify-package` checks catalog, research, six decks and all 722 decoded/hash-verified images |
| Large README and scattered phase files lacked a contributor map | Hard to locate implementation and understand assumptions | Concise entry page, extensive design guide, generated source index and historical checkpoint directory |

## Initial evidence

Before changes: 226 tests passed; dependency scanner reported no known vulnerable direct/transitive packages. Existing desktop lifecycle smoke passed but was found insufficient to validate current tab navigation. Existing source history and released 2.0 ZIP were retained during repair.

Detailed run evidence is written under `artifacts/release-audit-2.0.1/`. Final summarized records and cleanup inventory will be preserved here after acceptance.

## Final offline evidence

- 228 tests passed, zero failures or skips; all 12 solution projects built with warnings as errors and formatting clean.
- 13 independent acceptance probes passed. The canonical SQLite database reproduced byte-for-byte; research invariants passed. No known vulnerable direct/transitive dependencies were reported at audit time.
- Production WPF/lifecycle/proof-recovery/artwork checks passed. The additional visual matrix covered 120 state/size/scale cases and 365 screenshots, with no reported unclipped-text defects. This is synthetic logical scaling, not native DPI switching.
- The actual self-contained 2.0.1 executable verified 722 cards, 25,146 fusion pairs, all 722 artwork hashes/decodes, 39 research opponents and six reference decks from the organized Resources layout.
- Normal startup passed. An independently extracted copy with its database temporarily absent failed closed with exit code 1. No game or save write was involved.
- All 775 packaged files matched their hashes after independent ZIP extraction. The embedded Windows icon was extracted and inspected; the eleven-page illustrated guide was rendered and visually reviewed.
- 32 design sections, a 77-production-file navigation index and 25 offline HTML documents were generated, with local link targets checked. Runtime/library license texts are now included separately from game artwork attribution.

The self-check startup branch and remaining legacy snapshot references in the visual matrix both failed during development, were corrected, and passed fresh execution. Failed evidence was not relabeled as passing. Local detailed logs remain outside the player package; [compact audit receipt](releases/2.0.1-local-audit.json) captures acceptance.

The first GitHub run stopped at the database hash gate: the working dump had 2,374 CRLF pairs while Git's text checkout used LF. The source manifest intentionally records exact input bytes. `.gitattributes` now preserves the original dump bytes, matching the existing canonical database without altering card facts or weakening verification. The unpublished release tag was advanced to the repaired source before retrying publication; the failed run remains diagnostic history.

New unpaused changing-board gameplay and native OS-DPI transitions were **not retested**. This packaging audit does not supersede those earlier evidence boundaries. No optimizer scoring or proof identity changed in 2.0.1.

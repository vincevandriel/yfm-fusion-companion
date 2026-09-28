# Windows 2.1.0 release audit

Offline release acceptance: **PASS** (2026-09-28). Public download verification is recorded separately after the tagged GitHub build completes.

## Scope

Version 2.1 adds a read-only Free Duel farming reference to the Recommended Decks screen. The audit covers pinned source-data reproduction, complete reward-table validation, portrait provenance and decoding, card/duelist search, in-window details, recommended-deck farming links, package layout, documentation, and the existing optimizer and desktop regression suite.

The game, save, emulator configuration, and personal settings are outside the feature's write path. Package verification ran with emulator and personal-settings access disabled.

## Data and interface findings

- The bundled index contains all 39 Free Duel opponents, 117 named reward tables, and 8,666 reward entries. Every table has unique card IDs, positive weights, denominator 2,048, and weights summing exactly to 2,048.
- Missing recommended-deck cards link to the highest conditional drop chance, including opponent and result-rank group. The displayed percentage is conditional on earning that group; it does not estimate the chance of achieving the rank.
- Search suggestions remain hidden for zero through two characters and combine cards and duelists from three characters onward. Prefix matches lead, selection opens details in the same window, and card drop sources sort from highest to lowest conditional chance.
- All 39 portrait files are 48 by 48 pixels, hash-verified against the bundled manifest, and decoded during package self-check. Portrait and card-artwork rights remain separate from the MIT-licensed application code.
- The illustrated manual now includes current Recommended Decks, Free Duel gallery, duelist table, and card farming screens.

## Final offline evidence

- Locked restore, formatting, all 12 project builds, and **231 tests** passed with zero warnings or failures.
- The dependency scan reported no known vulnerable direct or transitive packages. The canonical 722-card, 25,146-pair SQLite catalog rebuilt byte-for-byte.
- Acceptance, production WPF rendering, desktop lifecycle, artwork, and documentation audits passed. The release renderer checks farm guidance, all 39 opponent tiles, three reward-table tabs, linked card details, three-character suggestions, and two-character suppression.
- The self-contained executable passed normal startup and package verification from the organized Resources layout: 722 card images, six reference decks, 39 research opponents, 39 Free Duel opponents, 117 reward tables, and 39 portraits.
- An independently extracted copy verified all **818 files** and failed closed when its database was removed. The Windows executable retains the original Forbidden Memories-inspired icon.
- Local candidate ZIP SHA-256: `B99F1320245FEF539689B77393CB5285F2D242B101D7943BBFF80DB2E2610BE6`.

The first packaging attempt correctly failed because the new reward index was placed inside the single-file executable instead of the external ResearchData directory. The engine content item now explicitly stays outside the single-file bundle; a fresh complete audit passed. Failed evidence remains diagnostic history and is not counted as acceptance.

New unpaused changing-board gameplay and native OS-DPI switching were not retested in this feature audit. Existing live-mode and optimizer evidence boundaries remain unchanged.

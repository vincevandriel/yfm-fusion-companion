# Phase 1 findings and Phase 2 verification boundary

Generated research data is intentionally separated from the application model until Phase 2 resolves the remaining mechanics and save-layout questions.

## Ready for implementation

- The public research dataset contains all 39 opponent IDs, names, opening-hand sizes, and weighted Deck pools from a revision-pinned MIT-licensed data/simulation project.
- The maximum-safety general objective covers duelists 1 through 32 and Duel Master K (39). The final gauntlet remains an explicit six-duelist special-configuration group: 33 through 38.
- The supplied USA disc audit establishes that ordinary fusion resolution uses the lower-ID packed-table record and its first matching higher-ID entry. It does not use a runtime primary/secondary/hidden-type priority hierarchy.
- A later optimizer must treat an equip interaction as terminal: a later monster fusion creates a new monster and discards the former equip effect.
- The research generator is deterministic when passed a fixed UTC timestamp, rejects unexpected 39-duelist/722-card source counts, and its verifier rejects invalid IDs, empty Deck pools, invalid card ranges, private paths, and an incomplete general-versus-gauntlet split.

## Still provisional

- Opponent deck weights and the source project's deck-construction procedure are suitable inputs to a threat model, but a displayed threat estimate is not a game-win probability.
- The low-mage/Neku field-spell behavior flag comes from the pinned reimplementation. Phase 2 must independently validate any behavior that changes a deck score.
- Guardian-star cycles and the +500 battle modifier are cross-checked by archival and community references. Phase 2 must validate the implementation boundary before presenting them as game-verified live advice.
- Community speedrun guides are useful for candidate archetypes, farming priorities, and practical card roles. They are never a substitute for the user's owned-card constraints or the concrete fusion table.

## Save-reader preparation

The present memory-card reader already validates a raw 128/256 KiB card image, its directory entry, duplicated `0x680`-byte save copies, deck cards, chest quantities, and library flags. It does not yet expose Star Chips or the Free Duel unlock mask.

Existing local reverse-engineering evidence identifies the live save-state fields as follows:

| Intended field | Live address | Relative offset from `0x801D0200` | Required Phase 2 proof |
|---|---:|---:|---|
| Star Chips | `0x801D07E0` | `0x5E0` | Read known displayed totals from at least two saves; confirm little-endian `uint32`; preserve read-only behavior. |
| Free Duel unlock mask | `0x801D06F4` | `0x4F4` | Read a known progress state; confirm MSB-first bit `0x80 >> (duelistId & 7)` against the game UI. |

These offsets are implementation targets, not permission to write to the game or save. If either independent proof fails, the companion must show the affected value as unavailable and leave the manual fallback intact.

## Phase 2 handoff

Phase 2 receives these artifacts:

- `docs/research/data/source_manifest.json`
- `docs/research/data/opponent_reference.json`
- `docs/research/data/guardian_star_rules.json`
- `docs/research/data/optimizer_policy.json`
- the existing disc-backed fusion-priority evidence and the current WPF/engine test suite.

It must produce: a verified save-parse extension, a concrete opponent-threat model, guardian-star resolution tests, an owned-card/star-chip purchase evaluator, and a maximum-safety optimizer with clear non-overclaiming labels.

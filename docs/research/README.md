# Research data

This directory is the Phase 1 evidence boundary for the campaign and opponent-aware optimizer. It stores compact, machine-readable facts and their provenance; it does not copy game images, executables, community-guide text, or third-party source code.

## Generated files

- `source_manifest.json` records source identity, source revision/hash where available, licence handling, intended use, and confidence level.
- `opponent_reference.json` contains all 39 duelists, opening hand sizes, Deck-pool weights, the explicit general-safety versus final-gauntlet scope, and clearly marked AI-related reimplementation claims.
- `guardian_star_rules.json` contains the two guardian-star cycles and display symbols. It is marked pending Phase 2 game verification.
- `optimizer_policy.json` records the requested maximum-safety objective, star-chip mode boundary, tie-break rule, and safety claims the application is forbidden to make.
- `generation_summary.json` is the machine-checkable count summary.

## Confidence labels

- `disc_verified` — extracted from the supplied USA game-image audit.
- `source_code_reimplementation` — supported by a version-pinned external data/simulation project; it must be independently checked before it controls a high-stakes mechanic.
- `community_consensus` — practical guide knowledge; useful for hypotheses and deck archetypes, not definitive game behavior.
- `community_consensus_pending_phase_2_game_verification` — shown to the user only with this limitation until Phase 2 closes the verification gap.

## Reproducibility

Regenerate the files using the pinned local checkout of `YGOFM-gamedata` and the disc-audit evidence. The generator fails when expected source counts change, so an upstream change cannot silently alter the optimizer's opponent model.

```powershell
& .\tools\Phase1Research\Generate-Phase1ResearchData.ps1 `
  -GameDataRoot '<game-data-root>' `
  -OutputRoot '.\docs\research\data' `
  -DiscAuditEvidence '<private-disc-audit-evidence.json>'
```

The public release must retain the source attribution in `THIRD_PARTY_NOTICES.md` before any derived YGOFM-gamedata facts are shipped.

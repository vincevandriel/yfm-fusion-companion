# Release verification and retention

- [2.0.1 audit](../RELEASE_2_0_1.md) records scope, repairs, results and limitations.
- [Local package receipt](2.0.1-local-audit.json) records the audited local candidate. GitHub builds from the same source with the same publisher; ZIP hashes may differ across builds.
- [Synthetic visual summary](2.0.1-visual-summary.json) records the additional state/scale matrix.

Public delivery and cleanup receipts are added after the tagged build has passed and its public archive has been downloaded and checked independently. They identify the authoritative public archive hash and removal totals. Git commits and tags remain available; personal preferences, proof checkpoints, game/save data and unrelated projects are retained.

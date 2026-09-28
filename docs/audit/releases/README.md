# Release verification and retention

Version **2.1.0** is the sole current GitHub release.

- [2.1.0 audit scope and findings](../RELEASE_2_1_0.md)
- [2.1.0 locally audited candidate](2.1.0-local-audit.json)
- [2.1.0 GitHub tagged-build acceptance](2.1.0-github-audit.json)
- [2.1.0 independent public download verification](2.1.0-public-verification.json)
- [2.1.0 publication and cleanup receipt](2.1.0-retirement.json)

- [Audit scope and findings](../RELEASE_2_0_1.md)
- [Locally audited candidate](2.0.1-local-audit.json)
- [GitHub tagged-build acceptance](2.0.1-github-audit.json)
- [Independent public download verification](2.0.1-public-verification.json)
- [Synthetic visual matrix](2.0.1-visual-summary.json)
- [Retirement and retention receipt](2.0.1-retirement.json)

The authoritative 2.1.0 public ZIP SHA-256 is `A92792CDB0589A203BC4DB555F332C6D31CBC49FEE69E3C5482D3F8E335FE79C`. Its 819 files were verified against the internal manifest after a fresh download. The downloaded executable passed its offline content self-check and normal startup locally; the same package passed normal startup and missing-resource rejection in GitHub Actions.

The v2.0.1 GitHub release and two redundant binary Actions artifacts were removed after 2.1.0 verification; historical Git source and compact audit records remain. Twelve local generated audit, probe, screenshot, candidate-package and public-download directories were removed. The desktop shortcut targets the verified 2.1.0 package. Automatic command policy rejected recursive deletion of the older installed 2.0.1 directory, so that inactive folder remains and is recorded in the cleanup receipt.

The local candidate and GitHub build have separate archive hashes because build outputs and documentation differ. Each was independently checked; the public receipt identifies the installed distribution.

Retirement removed 144 local package/build locations (18,238,467,702 bytes), the three previous GitHub releases and 13 redundant binary Actions artifacts (1,829,110,114 remote bytes in total). Git source history remains. A verified 3,122,134-byte local archive preserves 2,746 historical records as 725 unique objects, including older source changes absent from Git. Original source paths map to those objects in the archive index.

Runtime extraction caches and temporary audit copies remain because automatic approval review blocked the final cleanup command. Personal settings, proof files, games, saves and Codex recovery files were outside the deletion scope.

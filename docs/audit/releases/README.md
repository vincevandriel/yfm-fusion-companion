# Release verification and retention

Version **2.0.1** is the sole current GitHub release.

- [Audit scope and findings](../RELEASE_2_0_1.md)
- [Locally audited candidate](2.0.1-local-audit.json)
- [GitHub tagged-build acceptance](2.0.1-github-audit.json)
- [Independent public download verification](2.0.1-public-verification.json)
- [Synthetic visual matrix](2.0.1-visual-summary.json)
- [Retirement and retention receipt](2.0.1-retirement.json)

The authoritative public ZIP SHA-256 is `BE16C90E87BF65776807AB999FBC88C9867524DD428507DD76415852147E1CC1`. It contains 776 verified files. The public archive was downloaded without authentication and matched its checksum, GitHub digest and successful CI audit receipt. The CI executable passed its content self-check, normal startup and missing-resource rejection. Automatic approval review blocked launching the downloaded copy locally; no local launch is claimed.

The local candidate and GitHub build have separate archive hashes because build outputs and documentation differ. Each was independently checked; the public receipt identifies the installed distribution.

Retirement removed 144 local package/build locations (18,238,467,702 bytes), the three previous GitHub releases and 13 redundant binary Actions artifacts (1,829,110,114 remote bytes in total). Git source history remains. A verified 3,122,134-byte local archive preserves 2,746 historical records as 725 unique objects, including older source changes absent from Git. Original source paths map to those objects in the archive index.

Runtime extraction caches and temporary audit copies remain because automatic approval review blocked the final cleanup command. Personal settings, proof files, games, saves and Codex recovery files were outside the deletion scope.

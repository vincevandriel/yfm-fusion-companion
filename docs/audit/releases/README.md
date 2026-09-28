# Release verification and retention

Version **2.1.0** is the current audited candidate. Its tagged GitHub build and public download records are added here after publication.

- [2.1.0 audit scope and findings](../RELEASE_2_1_0.md)
- [2.1.0 locally audited candidate](2.1.0-local-audit.json)

- [Audit scope and findings](../RELEASE_2_0_1.md)
- [Locally audited candidate](2.0.1-local-audit.json)
- [GitHub tagged-build acceptance](2.0.1-github-audit.json)
- [Independent public download verification](2.0.1-public-verification.json)
- [Synthetic visual matrix](2.0.1-visual-summary.json)
- [Retirement and retention receipt](2.0.1-retirement.json)

The 2.0.1 records remain as historical audit evidence until the verified 2.1.0 public release is published and superseded binary assets are retired. The authoritative 2.0.1 public ZIP SHA-256 is `BE16C90E87BF65776807AB999FBC88C9867524DD428507DD76415852147E1CC1`.

The local candidate and GitHub build have separate archive hashes because build outputs and documentation differ. Each was independently checked; the public receipt identifies the installed distribution.

Retirement removed 144 local package/build locations (18,238,467,702 bytes), the three previous GitHub releases and 13 redundant binary Actions artifacts (1,829,110,114 remote bytes in total). Git source history remains. A verified 3,122,134-byte local archive preserves 2,746 historical records as 725 unique objects, including older source changes absent from Git. Original source paths map to those objects in the archive index.

Runtime extraction caches and temporary audit copies remain because automatic approval review blocked the final cleanup command. Personal settings, proof files, games, saves and Codex recovery files were outside the deletion scope.

# Automatic card artwork acceptance

2026-09-26. Imported all 722 images from hzrqftr/yugioh-forbiddenmemories, revision c50e070636fed6a0b2a0d6a9f2f88f79f07d58c2. Read assets and metadata only; no upstream scripts or repository instructions executed. All card-number/name mappings match the companion SQLite catalog, including the documented Kuwagata alpha/a alias at card 480.

WebP files were decoded into PNG for built-in WPF support. All 722 converted files passed exact decoded RGBA-byte equality checks. Original source URLs, image provenance, upstream notice, source revision, archive hash and per-file hashes/dimensions are included in Artwork. Attribution distinguishes artwork copyright from CC BY-SA metadata and MIT companion code.

Production rows automatically resolve their bundled images. Explicit custom files take precedence over custom folders; missing or unreadable overrides recover to bundled files. Gallery images preserve their aspect ratio, and card-number placeholders appear only when artwork is unavailable. Manual owned quantities remain separate from image setup.

Verification: all 722 images decoded through the production row/cache with no artwork setting; override precedence and absent/unreadable override recovery passed. Retained thumbnail cache stayed bounded (38,067,536 bytes / 67,108,864 bytes in the focused audit). Production WPF gallery rendering reviewed. Entire Release test suite: 218 passed, none failed/skipped. Windows publish has zero build warnings/errors; executable remained alive during its four-second isolated startup check. All 743 packaged files matched after independent ZIP extraction; all 722 published image hashes matched the import manifest. Source/test hashes remained unchanged from the final test checkpoint through packaging. Clean publish explicitly includes campaign ResearchData along with Artwork and Data.

Evidence: artifacts/artwork-audit/artwork-verification.json, artifacts/ui-render-artwork/artwork-audit.json, artifacts/ui-render-artwork/automatic-card-artwork.png, artifacts/artwork-tests/artwork.trx, artifacts/artwork-package-verification.json, assets/card-artwork/manifest.json. Gameplay coexistence and actual OS-DPI changes were not retested for this artwork update.

Package: YFM-Fusion-Companion-2.0-Artwork-20260926-122929.zip
SHA256: D2A17CC00D37E2403C1ECE64E312E777E6F9D9CCBF5EA86CC4F7A7DB2C9274E3

Rebuild with tools/Publish-CardArtwork.ps1. Re-import the pinned source with tools/Import-CardArtwork.py (Pillow required for import only; the Windows app requires no image library installation).

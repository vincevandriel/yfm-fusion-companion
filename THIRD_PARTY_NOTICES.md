# Third-party notices

YFM Fusion Companion is an independent, fan-made utility. Yu-Gi-Oh!, Yu-Gi-Oh! Forbidden Memories, card names, game rules, and related properties belong to their respective owners and are not relicensed under this repository's MIT License. PlayStation is a trademark of Sony Interactive Entertainment Inc. RetroArch, Libretro, and SwanStation are separate projects with their own licenses.

No PlayStation BIOS, game ROM/disc image, emulator, or memory-card save is distributed. Bundled card artwork has separate attribution and rights as described below. This project is not affiliated with or endorsed by Konami, Sony, RetroArch, Libretro, or the SwanStation authors.

## Included runtime and libraries

The self-contained application includes .NET Runtime and Windows Desktop 9.0.18 (MIT and their third-party components), Microsoft.Data.Sqlite/Core 9.0.20 (MIT), SQLitePCLRaw core/provider/bundle/native packaging 2.1.12 (Apache-2.0, Copyright 2014-2024 SourceGear, LLC), and the native SQLite implementation (public domain). System.Memory 4.5.3 is a transitive MIT-licensed Microsoft dependency; applicable implementation may be supplied by the shared runtime during publish. Full upstream license texts and .NET notices are in [dependency licenses](licenses/dependencies/README.md), copied into Licenses/Dependencies in the portable package. Package versions and content hashes are retained in the project lock files.

## YGOFM-gamedata derived research facts

The generated opponent-reference research data derives duelist names, opening hand sizes, Deck-pool weights, and a deck-generation reference from [sg4e/YGOFM-gamedata](https://github.com/sg4e/YGOFM-gamedata), revision `bd91b5b0568059ec38555a3994c30a5068dc3dda`. The upstream project is Copyright 2020 sg4e and licensed under the MIT License. This repository does not copy its source code, ROM data, or game image; it retains source revision and input hashes in `docs/research/data/source_manifest.json`.

## Bundled card artwork

The `Resources/Artwork` directory (source: `assets/card-artwork`) contains 722 card images from [hzrqftr/yugioh-forbiddenmemories](https://github.com/hzrqftr/yugioh-forbiddenmemories), pinned to c50e070636fed6a0b2a0d6a9f2f88f79f07d58c2 and converted from WebP to PNG without changing pixels. Artwork remains copyright Konami and its licensors and is not MIT-licensed. Per-card source records, conversion hashes, [attribution](assets/card-artwork/ATTRIBUTION.md), and the original notice accompany the assets. The image manifest metadata is CC BY-SA 4.0, attributed to Yugipedia contributors.

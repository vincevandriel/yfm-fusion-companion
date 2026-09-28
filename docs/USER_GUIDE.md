# YFM Fusion Companion user guide

A native, English-only Windows companion for Yu-Gi-Oh! Forbidden Memories. It works beside RetroArch/SwanStation and does not use a browser. Manual fusion, deck analysis, and optimization work without an emulator; automatic live/save features have the compatibility limits described below.

The **Owned-Card Optimizer → Recommended Decks** screen offers six community-informed 40-card builds, each with a distinct artwork icon and the number of required copies already owned beneath it. Every missing entry names its best Free Duel opponent, required rank group and exact conditional drop percentage. The linked Free Duel Database contains all 39 opponents, their portraits and complete reward tables. See [advanced deck research](research/FAN_DECK_RESEARCH.md) for deck evidence, scoring choices and limitations; setup availability is not a win rate.

Suggested deck cards default to **Alphabetical** order. Use **Card Order** above the result list to choose **Card number**, **ATK**, or **DEF**. Numbers sort lowest first; ATK and DEF sort highest first. Your selection is remembered and only changes the displayed order.

<!-- TOC -->

- [Download and start](#download-and-start)
- [Live RetroArch connection](#live-retroarch-connection)
- [Run the desktop application](#run-the-desktop-application)
- [Interface instruction manual](#interface-instruction-manual)
  - [Card entry and autocomplete](#card-entry-and-autocomplete)
  - [Recommended Decks and Free Duel Database](#recommended-decks-and-free-duel-database)
  - [TURN ADVISER tab](#turn-adviser-tab)
  - [DECK ANALYZER tab](#deck-analyzer-tab)
  - [LIVE DUEL tab](#live-duel-tab)
  - [OWNED-CARD OPTIMIZER tab](#owned-card-optimizer-tab)
  - [COMPACT LIVE mode](#compact-live-mode)
  - [Shared header controls and status meanings](#shared-header-controls-and-status-meanings)
- [Build the embedded database](#build-the-embedded-database)
- [Build, test and release](#build-test-and-release)
- [Safety boundary](#safety-boundary)
- [Fusion rule boundary](#fusion-rule-boundary)
- [Probability boundary](#probability-boundary)
- [Optimization boundary](#optimization-boundary)
- [Saved-snapshot boundary](#saved-snapshot-boundary)
- [Live boundary](#live-boundary)
- [Illustrated manual, license, and project status](#illustrated-manual-license-and-project-status)
- [Automatic card artwork](#automatic-card-artwork)

<!-- /TOC -->

## Download and start

1. Open this repository's **Releases** page and download a Windows x64 ZIP from the latest release. GitHub's automatic "Source code" archives are for developers and do not contain the ready-to-run program.
2. Extract the complete ZIP to a normal folder. Do not run files from inside the ZIP preview.
3. Keep Resources, Documentation and Licenses beside the executable. The app is self-contained and needs no separate .NET runtime. Start with START-HERE.txt.
4. Double-click `YFM Fusion Companion.exe`.

Windows 10 or 11 x64 is required. RetroArch and SwanStation are optional unless you want automatic save import or Live Duel. This project is not code-signed; if SmartScreen appears, use **More info > Run anyway** only after confirming the download and release SHA-256.

## Live RetroArch connection

The **LIVE DUEL** tab uses RetroArch's supported UDP Network Control Interface on `127.0.0.1:55355`. The companion's client is deliberately limited to status and memory-read requests. It does not expose input, cheat, save-state, write-memory, or arbitrary-command methods, and it does not scan or patch the RetroArch process.

To enable the RetroArch side manually, open **Settings → Network → Network Commands** and switch it on. The companion checks automatically every second and never rewrites `retroarch.cfg`. A green **UP TO DATE** indicator means the polling loop is receiving valid state; a red **ERROR** indicator means the latest update was unsuccessful.

RetroArch itself may bind Network Commands beyond loopback. Keep Windows Firewall blocking unsolicited inbound UDP 55355 from other computers. The companion detects and warns when the listener is visible on a non-loopback address.

During a supported NTSC-U duel, the live view validates guest RAM before displaying it, then reads the ordered five-card hand, shuffled deck, separate monster and spell/trap zones for both players, terrain, power modifiers, and both life totals. Advice-relevant hand and field regions are sampled twice and must agree; if the duel changes between RetroArch's separate memory reads, advice is withheld for that refresh instead of associating a slot with the wrong card. It filters inactive field-slot records and automatically recomputes legal fusion routes. Compatible equips are treated as terminal interactions only, so an equipped monster is never assumed to retain its bonus after becoming a fusion material.

## Run the desktop application

For the portable Windows build, extract the entire ZIP and double-click `YFM Fusion Companion.exe`. Keep the `Resources` folder beside the executable. No installation, browser, web server, account, or Internet connection is required.

The application opens in the full resizable analysis workspace. Choose **COMPACT LIVE** for a 272-by-1002 logical-pixel adviser docked to the right edge of the Windows work area. It shows only the highest-ranked legal routes so it can occupy the unused side region beside a maximized duel. Compact routes use hand-slot numbers (`1+2+3`) and prefix one targeted field position with `F` (`F(3)+2+5`); monster positions are `F(1)` through `F(5)`, and spell/trap positions are `F(6)` through `F(10)`. Its size is bounded. Names wrap to two lines without sideways scrolling; guardian symbols are 20 logical pixels beneath Result. Warm glow identifies selectable stars, and red glow highlights known opponent relationships. Windows DPI scaling changes physical pixel size. **Always on top**, compact/full mode, the last window position and size, and the last validated save path are remembered locally for the next launch.

```powershell
dotnet run --project src/YfmCompanion.Desktop --configuration Release
```

The Windows WPF interface includes:

- five ordered hand slots, five monster-field slots, and five spell/trap-field slots;
- card-name and card-number autocomplete with top-result Tab completion;
- full-width recommendation and live-card tables, plus an optional Live Duel card inspector that slides over the right edge only when opened;
- strongest-first legal turn routes, including secondary and tertiary hand fusions and one terminal field interaction;
- forty deck slots and exact enumeration of all 658,008 physical five-card combinations in a complete deck;
- one-click re-reading of the current saved memory-card deck into all forty Deck Analyzer slots;
- per-result probabilities, strong fusion/equip thresholds, expected best final ATK, dead-hand probability, progress, and cancellation.
- a searchable 722-card owned-inventory editor with quantities;
- balanced, fusion-consistency, maximum-power, control, field/type, and ritual-experiment strategy profiles;
- opponent-type and preferred-field inputs, exact finalist probabilities, deterministic output, inclusion reasons, key outcomes, and low-value exclusions.
- automatic SwanStation save discovery plus manual `.srm` / `.mcr` selection;
- exact source filename and saved timestamp, explicit **Saved snapshot (not live)** labeling, and read-only validation;
- constructed-deck, chest, deck-copy, total-owned, and Library views;
- one-click transfer of the saved 40-card deck to Deck Analyzer and saved chest-plus-deck totals to Owned-card optimizer.
- explicit `LIVE`, `NOT IN DUEL`, `SAVED SNAPSHOT`, `MANUAL`, `NO CONTENT`, `WRONG GAME`, `UNAVAILABLE`, and `DISCONNECTED` state badges;
- a compact live adviser and a normal resizable analysis workspace;
- an on-demand diagnostics export containing application and connection statuses only, never card, hand, deck, collection, emulator-memory, or gameplay values.

## Interface instruction manual

### Card entry and autocomplete

Card boxes accept a card name or card number. Suggestions narrow with every character. Click a suggestion to accept it, or press **Tab** to accept the top suggestion; if only one match remains, Tab fills it and moves to the next box. Empty boxes are valid, and duplicate physical cards use separate slots.

### Recommended Decks and Free Duel Database

Open **Owned-Card Optimizer → Recommended Decks**. The first tab keeps the six researched 40-card builds, ownership counts, strategy notes and exact opening-hand check. Each card row now includes **BEST FARM** when copies are missing: opponent, required result rank and the exact table probability. A percentage is conditional on earning that named rank group; it is not an unconditional per-duel chance. Double-click a row to inspect the full card.

Choose **OPEN FREE DUEL DATABASE** or its tab to browse all 39 opponents. Each portrait tile opens an in-window popup with a larger portrait and separate complete tables for **S/A POW**, **S/A TEC**, and **B/C/D**. Every table totals 2,048 weight. Double-click a reward-table card to open its card profile.

The search box is shared by both tabs. After the third typed letter it predicts matching card and duelist names. Selecting a duelist opens their reward popup. Selecting a card opens its full artwork, number, type, attribute, level, ATK/DEF, guardian stars, description, password-shop availability, password and Star Chip cost when available, plus every duelist/rank source ordered from highest to lowest conditional drop percentage. Duelist buttons inside that list return directly to the opponent popup. All data and images are bundled for offline, read-only use.

### TURN ADVISER tab

Use this tab to find the strongest legal action from an exact hand and your current field.

- **HAND - ordered slots 1-5:** enter up to five current hand cards. The adviser tries every legal start and order, including secondary and tertiary fusions.
- **MONSTER FIELD:** optionally enter your five occupied monster positions.
- **SPELL / TRAP FIELD:** optionally enter your five occupied back-row positions.
- **ANALYZE TURN:** lists valid routes by final ATK and then DEF. Result is the final card; Hand Order is the required hand-slot selection order; Route shows every intermediate result; Field names the single occupied field position used; Glitch identifies a documented glitch outcome.
- **Include documented glitch fusions:** controls whether known glitched game-data results are eligible.
- **CLEAR:** empties the manual position.

When a field card is used, it interacts with the first selected hand card and that result continues through later hand cards. A route cannot use a second occupied field position. Non-fusions that merely discard a card are omitted. Compatible equips are terminal because their bonus disappears if the equipped monster is fused again.

### DECK ANALYZER tab

Enter up to 40 cards or choose **LOAD CURRENT DECK FROM SAVE** to read the current validated memory-card save directly. **ANALYZE ALL 5-CARD HANDS** checks every physical five-card combination; a full deck contains exactly 658,008 such hands. Copies are distinct and draws are without replacement. **CANCEL** safely stops a calculation and **CLEAR** empties the slots. The save status shown below the controls reports the selected file and whether a complete 40-card deck was available.

- **ANY FUSION / EQUIP:** chance of at least one modeled valid result.
- **ATK >= 2500 / 2800 / 3000:** chance that the best obtainable result reaches the threshold.
- **EXPECTED BEST ATK:** average of the best result obtainable from each hand.
- **DEAD HAND:** chance that no modeled fusion or compatible terminal equip exists.
- The results table shows each result, effective final ATK/DEF, hand chance, number of qualifying hands, and one representative route. A result is counted once per hand even if several orders make it.

### LIVE DUEL tab

Live Duel updates automatically every second. There is no refresh button. For the validated connection, open RetroArch **Settings > Network**, enable **Network Commands**, set **Network Command Port** to `55355`, leave **Network RetroPad** and **stdin Commands** off, restart RetroArch, and start the NTSC-U game with SwanStation.

- **CONNECTION:** reports whether RetroArch, the game, and duel state passed structural validation.
- **PLAYER LP / OPPONENT LP / TERRAIN:** current validated duel values.
- **UPDATE STATUS:** green **UP TO DATE** while reads continue and red **ERROR** after the latest read fails.
- **HAND, YOUR MONSTERS, YOUR SPELLS / TRAPS, OPPONENT FIELD:** active live positions. Unknown face-down information is not guessed.
- **LIVE ADVICE:** strongest legal routes from the current validated hand and board.
- **STAR 1 / STAR 2 / VS FIELD:** each available guardian-star chain is shown beside a live recommendation. `F#?` means the opponent card's active guardian star or battle position is not verified by the read-only live mapping, so the companion deliberately does not guess a win, tie, or loss. Where those values are verified, `✓`, `=`, and `×` mean a favourable battle, tie, and unfavourable battle for that field slot.
- **CONSTRUCTED DECK / OWNED COLLECTION:** save-derived deck and ownership information when available.
- **INSPECT CARDS:** opens the optional right-side inspector for the selected live card. It shows type, ATK, DEF, and expandable advanced catalog data including categories, fusion partners, recipes, and compatible equips.

The client sends only status and read-memory commands to `127.0.0.1`. Keep Windows Firewall blocking unsolicited inbound UDP 55355 from other computers because RetroArch itself may listen beyond loopback.

### OWNED-CARD OPTIMIZER tab

Enter owned quantities manually or load them with **REFRESH** / **CHOOSE SAVE FILE…** in the optimizer's source panel. The same panel reports the selected save, owned totals, Star Chips, and validation status; no separate Save Snapshot tab is needed. The search filters by any name letters or card number. **SET VISIBLE TO 3** changes only currently filtered rows; **CLEAR OWNED** resets quantities. At least 40 usable copies are required, and output never exceeds ownership, the normal three-copy limit, or the one-copy Exodia-piece limits.

For the campaign choices, select **General campaign**, **One specific opponent**, or **Final gauntlet**. The general plan favours the number of opponent threat sets with at least one modeled answer, then protects its weakest modeled matchup and its opening-hand answer coverage before power tie-breaks. This is matchup guidance, not a guaranteed win-rate or a claim about unverified CPU guardian-star choices. The threat model includes direct and material-limited chained fusion threats; unknown battle position, terrain behavior, or CPU star selection remains labeled as uncertain rather than inferred.

When a validated save exposes Star Chips, **USE SAVED STAR CHIPS** enables a *virtual* shopping plan. It first reserves enough affordable, distinct legal cards to make a 40-card deck possible, then compares optional upgrades against the owned-only deck and retains the zero-spend plan unless spending is strictly better under the same safety objective. Password cards must have a numeric eight-digit password. The game save does not record whether a card password was previously redeemed, so enter any already redeemed names in **ALREADY REDEEMED PASSWORD CARD NAMES**; those names are excluded. The application never spends chips, redeems passwords, or changes the save.

Profiles mean:

- **Balanced:** fusion consistency, compatible field/equip setups, natural strong monsters, removal and draw reliability.
- **Fusion consistency:** overlapping materials and independent routes for weak draws.
- **Maximum power:** strongest reachable results and standalone monsters.
- **Control and safety:** removal, broad traps, stall, and debuffs.
- **Field and type:** selected field/type advantages and penalties.
- **Ritual experiment:** deliberate ritual-package testing; rituals remain disfavored in normal profiles because they require an exact ritual and three established monsters.

**YOUR FOCUS TYPES** and **OPPONENT TYPES** accept comma-separated types. **FIELD** selects Automatic/none, Forest, Wasteland, Mountain, Sogen, Umi, or Yami. **BUILD OPTIMAL 40-CARD DECK** scores legal candidates, screens sampled hands reproducibly, and exactly enumerates every five-card hand for finalists. Output tabs explain the optimized list, key outcomes, ownership limits, and low-value owned cards left out.

### COMPACT LIVE mode

**COMPACT LIVE** creates a 272-by-1002 logical-pixel sidecar at the right work-area edge. It shows only Best Legal Routes plus **PIN** and **FULL**, has fixed bounds, and restores prior full-window bounds when closed. Result and effective ATK are followed by compact selection notation: `1+2+3` means hand slots; `F(1)` through `F(5)` are monster positions; `F(6)` through `F(10)` are spell/trap positions. For example, `F(3)+2+5` starts with monster position 3 and continues with hand slots 2 and 5. Card names wrap to two lines without sideways scrolling. The 20px symbols beneath Result show available guardian stars. Warm glow highlights selectable stars; red glow marks known opponent relationships. Unknown live battle information remains explicitly unknown. Windows DPI scaling changes physical pixel dimensions.

### Shared header controls and status meanings

- **Always on top / PIN:** keeps the companion above the game.
- **EXPORT DIAGNOSTICS:** writes a local status/error report only when requested; it excludes card, hand, deck, collection, gameplay, and emulator-memory values.
- **MANUAL:** user-entered data. **SAVED SNAPSHOT:** validated disk save. **LIVE:** validated active duel. **NOT IN DUEL:** RetroArch responded outside a duel. **NO CONTENT, WRONG GAME, UNAVAILABLE, DISCONNECTED:** live information was rejected safely and manual features remain usable.

## Build the embedded database

```powershell
dotnet run --project tools/YfmCompanion.DataBuilder -- "database-source\YuGiOh_Forbidden_Memories_PostgreSQL.sql" "artifacts\yfm.db"
```

The importer accepts only literal `INSERT` data from the known data tables. PostgreSQL functions, comments, procedural blocks, and all other document content are ignored. It validates the source QA counts and SQLite foreign keys before replacing the output database.

## Build, test and release

See [CONTRIBUTING.md](../CONTRIBUTING.md) for reproducible build and audit commands. The current release uses `tools/Publish-Release.ps1` locally and in GitHub Actions. It refuses existing outputs and records separate correctness, UI, package and public-download evidence.

## Safety boundary

The application reads RetroArch configuration only to locate save folders and the configured Network Commands port, opens memory-card files with read sharing, and sends only status and read-memory requests to RetroArch on this computer. It never writes a save, configuration, ROM, or process memory and never sends game input. The generated SQLite database is derived from the supplied PostgreSQL data and is read-only at application runtime. Saved snapshots and live RetroArch state are labeled separately.

Desktop preferences are saved in `%LOCALAPPDATA%\YFM Fusion Companion\settings.json`. They include window choices, source paths, worker choices and card ordering. Explicit proof searches also write recoverable checkpoint files; no game files are changed. Diagnostics remain in memory until **EXPORT DIAGNOSTICS** is deliberately chosen; an export contains statuses and error text only. There is no telemetry, update checker, advertising, analytics, remote API, or Internet client.

## Fusion rule boundary

The tactical planner:

- accepts up to five hand, five monster-field, and five spell/trap-field slots;
- searches all usable hand starting points and fusion orders;
- records every intermediate result and the exact hand-slot selection order;
- when targeting an occupied field slot, starts with that field card, interacts it with the first selected hand card, and then carries the resulting hand accumulator through later selected hand cards;
- allows only the initial occupied field target, preventing a second field target later in the chain;
- keeps duplicate cards distinct by slot;
- includes or excludes glitch fusions explicitly;
- models a compatible equip as a terminal interaction: Megamorph adds `+1000 ATK / +1000 DEF`; other compatible equips add `+500 ATK / +500 DEF`.

The live integration supplies the player monster and spell/trap zones separately. Controlled NTSC-U testing verified those zones, field-first fusion order, fusion-result replacement, destroyed-card filtering, Sogen terrain activation, and a terminal Beast Fangs equip.

## Probability boundary

The deck analyzer treats duplicate copies as distinct physical cards and enumerates combinations without replacement. For each random hand it searches all card starting points and all legal multi-step fusion orders, counts an outcome at most once per hand, and ranks displayed results by effective ATK then DEF. Compatible equip interactions count as successful fusion opportunities. An equip is always terminal: its bonus applies to the final equipped monster, and the analyzer never carries that bonus through a subsequent monster fusion.

## Optimization boundary

The owned-card optimizer always produces exactly 40 cards when the collection can supply 40 legal copies. It never exceeds an entered quantity, the ordinary three-copy limit, or the individual one-copy limit for each Exodia piece. Candidate decks are built from fusion-partner breadth, reachable strength, continuation potential, standalone value, compatible final equips, selected field/type interactions, and researched utility-card roles. Reproducible sampled hands screen the candidates; every physical five-card hand is then enumerated for the finalists.

The final decision uses the documented objective and deterministic tie-breaks. Balanced scoring values fusion consistency, coherent compatible support, 3,500+ and endgame power, natural bodies, removal and draw reliability. Campaign objectives additionally use modeled opponent coverage and safety. These metrics are not win rates. See [scoring design](DESIGN.md#16-objective-ordering-and-score-versions). Ritual cards remain experimental because their exact field setup is not an ordinary hand fusion.

## Saved-snapshot boundary

The save reader accepts raw 128 KiB and 256 KiB PlayStation memory-card images. It finds the NTSC-U `BASLUS-01411-YUGIOH` directory entry in any block or bank, verifies the PlayStation directory checksum, requires the `SC` save-block signature and one-block size, and requires both internal `0x680`-byte game save copies to match. The game-loaded first copy is then used to read:

- 40 little-endian constructed-deck card IDs;
- 722 chest quantity bytes;
- total ownership as chest copies plus copies currently in the constructed deck;
- 722 Library-seen flags.

Malformed, empty, torn, unsupported, or temporarily locked files return a visible failure and leave every manual feature operational. The Library flags are informational: the game updates them lazily when the Library is opened, and an emulator may not flush a fresh save to disk until the game closes.

The displayed deck and owned collection were compared with the game and accepted during validation. Live RAM order is not treated as constructed-deck order; copy-count membership is the meaningful live/save comparison.

## Live boundary

The live reader supports the NTSC-U Forbidden Memories content through RetroArch/SwanStation Network Commands. Controlled runtime testing verified the five-card hand, hand changes after draws and plays, both field sides, separate player monster and spell/trap zones, Life Points, Sogen terrain, the independent `+500` Beast Fangs power modifier, and inactive records left behind by destroyed monsters.

The game leaves duel structures populated after returning to story mode, so the reader uses the duel-screen mode byte at `0x9B26C`; value `0xC3` is required before any duel-only state is displayed. The halfword at `0x9B23A` is an action word that changes throughout a duel and is retained only for diagnostics. Title/startup without loaded save data, story mode, Close Content, complete RetroArch shutdown, and reconnection after restart are separate safe states. Save-state testing is intentionally not required because RetroAchievements Hardcore Mode disables save states and the companion does not depend on them.

## Illustrated manual, license, and project status

The labeled PDF booklet is [`output/pdf/YFM-Fusion-Companion-User-Guide.pdf`](../output/pdf/YFM-Fusion-Companion-User-Guide.pdf). The portable package includes this illustrated PDF and offline HTML versions of the user and design guides.

The audited catalog contains 722 cards and 25,146 resolved fusion pairs. Each tagged release regenerates the guide and runs its full correctness and acceptance checks. The included Release-audit.json records the actual count and scope for that package.

Original companion code is released under the [MIT License](../LICENSE). Game names, card names, rules, and other third-party properties remain with their owners and are not relicensed. No ROM, BIOS, emulator, or save is included. All 722 card images are now bundled for automatic offline display; see [artwork attribution](../assets/card-artwork/ATTRIBUTION.md) for their separate provenance and rights. See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md). This fan project is not affiliated with or endorsed by Konami, Sony, RetroArch, Libretro, or SwanStation.

## Automatic card artwork

The Owned Collection displays all 722 cards with built-in images. No folder selection or image download is required. Custom artwork buttons are optional overrides. Card IDs map to bundled `Resources/Artwork/001.png` through `Resources/Artwork/722.png`; keep Resources with the Windows executable. See [artwork attribution](../assets/card-artwork/ATTRIBUTION.md).

# YFM Fusion Companion

A native, English-only Windows companion for Yu-Gi-Oh! Forbidden Memories. It works beside RetroArch/SwanStation and does not use a browser. Manual fusion, deck analysis, and optimization work without an emulator; automatic live/save features have the compatibility limits described below.

## Download and install dependencies first

1. Open this repository's **Releases** page and download the latest `yfm_companion_windows_x64_*.zip`. GitHub's automatic "Source code" archives are for developers and do not contain the ready-to-run program.
2. Extract the complete ZIP to a normal folder. Do not run files from inside the ZIP preview.
3. Double-click `install_dependencies.cmd`. It verifies 64-bit Windows and confirms that the executable and `Data\yfm.db` remained together. The portable release is self-contained, so it normally installs nothing and reports that no separate .NET runtime is required.
4. Double-click `YFM Fusion Companion.exe`.

Windows 10 or 11 x64 is required. RetroArch and SwanStation are optional unless you want automatic save import or Live Duel. This project is not code-signed; if SmartScreen appears, use **More info > Run anyway** only after confirming the download and release SHA-256.

## Live RetroArch connection

The **LIVE DUEL** tab uses RetroArch's supported UDP Network Control Interface on `127.0.0.1:55355`. The companion's client is deliberately limited to status and memory-read requests. It does not expose input, cheat, save-state, write-memory, or arbitrary-command methods, and it does not scan or patch the RetroArch process.

To enable the RetroArch side manually, open **Settings → Network → Network Commands** and switch it on. The companion checks automatically every second and never rewrites `retroarch.cfg`. A green **UP TO DATE** indicator means the polling loop is receiving valid state; a red **ERROR** indicator means the latest update was unsuccessful.

RetroArch itself may bind Network Commands beyond loopback. Keep Windows Firewall blocking unsolicited inbound UDP 55355 from other computers. The companion detects and warns when the listener is visible on a non-loopback address.

During a supported NTSC-U duel, the live view validates guest RAM before displaying it, then reads the ordered five-card hand, shuffled deck, separate monster and spell/trap zones for both players, terrain, power modifiers, and both life totals. Advice-relevant hand and field regions are sampled twice and must agree; if the duel changes between RetroArch's separate memory reads, advice is withheld for that refresh instead of associating a slot with the wrong card. It filters inactive field-slot records and automatically recomputes legal fusion routes. Compatible equips are treated as terminal interactions only, so an equipped monster is never assumed to retain its bonus after becoming a fusion material.

Native Windows companion project for *Yu-Gi-Oh! Forbidden Memories*.

The final implementation contains the completed Phase 1 data pipeline and resolver, Phase 2 tactical planner, Phase 3 native manual-entry interface, Phase 4 exact deck probability analyzer, Phase 5 owned-card deck optimizer, Phase 6 read-only saved-snapshot importer, Phase 7 read-only RetroArch live connection, Phase 8 release integration, and the automated Phase 9 final audit. It remains entirely offline and English-only.

## Run the desktop application

For the portable Windows build, extract the entire ZIP and double-click `YFM Fusion Companion.exe`. Keep the `Data` folder beside the executable. No installation, browser, web server, account, or Internet connection is required.

The application opens in the full resizable analysis workspace. Choose **COMPACT LIVE** for a 320-pixel-wide adviser docked to the right edge of the Windows work area. It shows only the highest-ranked legal routes so it can occupy the unused side region beside a maximized duel. Compact routes use hand-slot numbers (`1+2+3`) and prefix one targeted field position with `F` (`F(3)+2+5`); monster positions are `F(1)` through `F(5)`, and spell/trap positions are `F(6)` through `F(10)`. Its minimum window size is disabled. **Always on top**, compact/full mode, the last window position and size, and the last validated save path are remembered locally for the next launch.

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

Enter up to 40 cards or choose **LOAD CURRENT DECK FROM SAVE** after validating a Save Snapshot. **ANALYZE ALL 5-CARD HANDS** checks every physical five-card combination; a full deck contains exactly 658,008 such hands. Copies are distinct and draws are without replacement. **CANCEL** safely stops a calculation and **CLEAR** empties the slots.

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

### SAVE SNAPSHOT tab

This tab opens memory-card files read-only. **REFRESH SAVED SNAPSHOT** searches configured SwanStation locations and reads the newest supported save again. **CHOOSE SAVE FILE...** accepts raw 128 KiB or 256 KiB `.srm`/`.mcr` images. Source, File, Saved, and Validation explain exactly what was read.

- **CONSTRUCTED DECK:** the 40 saved deck positions, when every position contains a valid card. A valid save with an empty or incomplete deck still imports its chest/collection; its Deck Analyzer load button stays disabled rather than treating empty slots as card IDs.
- **COLLECTION:** chest quantity, copies in the deck, total owned, and Library-seen flag.
- **LOAD THIS DECK INTO ANALYZER:** copies the saved deck into Deck Analyzer.
- **LOAD OWNED CARDS INTO OPTIMIZER:** copies chest-plus-deck totals into Owned-card Optimizer.

The load buttons transfer information only inside the companion; they never load or modify the game save. If recent progress is absent, save in-game, let the emulator flush the memory card (close content if necessary), and refresh again.

### OWNED-CARD OPTIMIZER tab

Enter owned quantities manually or load them from Save Snapshot. The search filters by any name letters or card number. **SET VISIBLE TO 3** changes only currently filtered rows; **CLEAR OWNED** resets quantities. At least 40 usable copies are required, and output never exceeds ownership, the normal three-copy limit, or the one-copy Exodia-piece limits.

For the campaign choices, select **General campaign**, **One specific opponent**, or **Final gauntlet**. The general plan favours the number of opponent threat sets with at least one modeled answer, then protects its weakest modeled matchup and its opening-hand answer coverage before power tie-breaks. This is matchup guidance, not a guaranteed win-rate or a claim about unverified CPU guardian-star choices. The threat model includes direct and material-limited chained fusion threats; unknown battle position, terrain behavior, or CPU star selection remains labeled as uncertain rather than inferred.

When a validated save exposes Star Chips, **USE SAVED STAR CHIPS** enables a *virtual* shopping plan. It first reserves enough affordable, distinct legal cards to make a 40-card deck possible, then compares optional upgrades against the owned-only deck and retains the zero-spend plan unless spending is strictly better under the same safety objective. Password cards must have a numeric eight-digit password. The game save does not record whether a card password was previously redeemed, so enter any already redeemed names in **ALREADY REDEEMED PASSWORD CARD NAMES**; those names are excluded. The application never spends chips, redeems passwords, or changes the save.

Profiles mean:

- **Balanced:** strong-fusion probability with control, equips, and secondary routes.
- **Fusion consistency:** overlapping materials and independent routes for weak draws.
- **Maximum power:** strongest reachable results and standalone monsters.
- **Control and safety:** removal, broad traps, stall, and debuffs.
- **Field and type:** selected field/type advantages and penalties.
- **Ritual experiment:** deliberate ritual-package testing; rituals remain disfavored in normal profiles because they require an exact ritual and three established monsters.

**YOUR FOCUS TYPES** and **OPPONENT TYPES** accept comma-separated types. **FIELD** selects Automatic/none, Forest, Wasteland, Mountain, Sogen, Umi, or Yami. **BUILD OPTIMAL 40-CARD DECK** scores legal candidates, screens sampled hands reproducibly, and exactly enumerates every five-card hand for finalists. Output tabs explain the optimized list, key outcomes, ownership limits, and low-value owned cards left out.

### COMPACT LIVE mode

**COMPACT LIVE** creates a 320-pixel-wide sidecar at the right work-area edge. It shows only Best Legal Routes plus **PIN** and **FULL**, has no enforced minimum size, and restores prior full-window bounds when closed. Result and effective ATK are followed by compact selection notation: `1+2+3` means hand slots; `F(1)` through `F(5)` are monster positions; `F(6)` through `F(10)` are spell/trap positions. For example, `F(3)+2+5` starts with monster position 3 and continues with hand slots 2 and 5. The two small lines below each route show the card's two guardian-star chains and field-slot outcomes; they never add material-card names or widen the compact panel, and use `F#?` whenever live battle information is unknown.

### Shared header controls and status meanings

- **Always on top / PIN:** keeps the companion above the game.
- **EXPORT DIAGNOSTICS:** writes a local status/error report only when requested; it excludes card, hand, deck, collection, gameplay, and emulator-memory values.
- **MANUAL:** user-entered data. **SAVED SNAPSHOT:** validated disk save. **LIVE:** validated active duel. **NOT IN DUEL:** RetroArch responded outside a duel. **NO CONTENT, WRONG GAME, UNAVAILABLE, DISCONNECTED:** live information was rejected safely and manual features remain usable.

## Build the embedded database

```powershell
dotnet run --project tools/YfmCompanion.DataBuilder -- "database-source\YuGiOh_Forbidden_Memories_PostgreSQL.sql" "artifacts\yfm.db"
```

The importer accepts only literal `INSERT` data from the known data tables. PostgreSQL functions, comments, procedural blocks, and all other document content are ignored. It validates the source QA counts and SQLite foreign keys before replacing the output database.

## Test

```powershell
$env:YFM_SQL_PATH = "$PWD\database-source\YuGiOh_Forbidden_Memories_PostgreSQL.sql"
dotnet test YfmFusionCompanion.sln --configuration Release
```

The current regression suite contains 165 automated tests. To reproduce the complete final audit—including strict formatting and analyzer checks, dependency vulnerability scanning, deterministic database rebuilding, tests with coverage evidence, feature and integration scenarios, full/compact UI rendering, self-contained publication, startup smoke testing, minimal package assembly, SHA-256 manifests, and an independent archive comparison—run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Invoke-Phase9Audit.ps1
```

The audit creates fresh timestamped evidence and refuses to overwrite an existing final directory or archive.

## Safety boundary

The application reads RetroArch configuration only to locate save folders and the configured Network Commands port, opens memory-card files with read sharing, and sends only status and read-memory requests to RetroArch on this computer. It never writes a save, configuration, ROM, or process memory and never sends game input. The generated SQLite database is derived from the supplied PostgreSQL data and is read-only at application runtime. Saved snapshots and live RetroArch state are labeled separately.

The only automatic file write is the user's desktop preference file at `%LOCALAPPDATA%\YFM Fusion Companion\settings.json`. It contains window bounds, compact/topmost choices, and the last selected save-file path. Diagnostics remain in memory until **EXPORT DIAGNOSTICS** is deliberately chosen; an export contains statuses and error text only. There is no telemetry, update checker, advertising, analytics, remote API, or Internet client.

## Phase 2 rule boundary

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

## Phase 4 probability boundary

The deck analyzer treats duplicate copies as distinct physical cards and enumerates combinations without replacement. For each random hand it searches all card starting points and all legal multi-step fusion orders, counts an outcome at most once per hand, and ranks displayed results by effective ATK then DEF. Compatible equip interactions count as successful fusion opportunities. An equip is always terminal: its bonus applies to the final equipped monster, and the analyzer never carries that bonus through a subsequent monster fusion.

## Phase 5 optimization boundary

The owned-card optimizer always produces exactly 40 cards when the collection can supply 40 legal copies. It never exceeds an entered quantity, the ordinary three-copy limit, or the individual one-copy limit for each Exodia piece. Candidate decks are built from fusion-partner breadth, reachable strength, continuation potential, standalone value, compatible final equips, selected field/type interactions, and researched utility-card roles. Reproducible sampled hands screen the candidates; every physical five-card hand is then enumerated for the finalists.

The default final decision is lexicographic: maximize the exact chance of a ≥2800 ATK fusion/equipped result, then ≥2500, then expected best final ATK, then any valid outcome. Ritual cards are excluded from normal profiles because Forbidden Memories rituals require a ritual plus three exact monsters established on the field; the opt-in ritual experiment does not count rituals as ordinary hand fusions.

## Phase 6 saved-snapshot boundary

The save reader accepts raw 128 KiB and 256 KiB PlayStation memory-card images. It finds the NTSC-U `BASLUS-01411-YUGIOH` directory entry in any block or bank, verifies the PlayStation directory checksum, requires the `SC` save-block signature and one-block size, and requires both internal `0x680`-byte game save copies to match. The game-loaded first copy is then used to read:

- 40 little-endian constructed-deck card IDs;
- 722 chest quantity bytes;
- total ownership as chest copies plus copies currently in the constructed deck;
- 722 Library-seen flags.

Malformed, empty, torn, unsupported, or temporarily locked files return a visible failure and leave every manual feature operational. The Library flags are informational: the game updates them lazily when the Library is opened, and an emulator may not flush a fresh save to disk until the game closes.

The displayed deck and owned collection were compared with the game and accepted during validation. Live RAM order is not treated as constructed-deck order; copy-count membership is the meaningful live/save comparison.

## Phase 7 live boundary

The live reader supports the NTSC-U Forbidden Memories content through RetroArch/SwanStation Network Commands. Controlled runtime testing verified the five-card hand, hand changes after draws and plays, both field sides, separate player monster and spell/trap zones, Life Points, Sogen terrain, the independent `+500` Beast Fangs power modifier, and inactive records left behind by destroyed monsters.

The game leaves duel structures populated after returning to story mode, so the reader uses the duel-screen mode byte at `0x9B26C`; value `0xC3` is required before any duel-only state is displayed. The halfword at `0x9B23A` is an action word that changes throughout a duel and is retained only for diagnostics. Title/startup without loaded save data, story mode, Close Content, complete RetroArch shutdown, and reconnection after restart are separate safe states. Save-state testing is intentionally not required because RetroAchievements Hardcore Mode disables save states and the companion does not depend on them.

## Illustrated manual, license, and project status

The labeled PDF booklet is [`output/pdf/YFM-Fusion-Companion-User-Guide.pdf`](output/pdf/YFM-Fusion-Companion-User-Guide.pdf). The Markdown README deliberately remains text-only.

The audited catalog contains 722 cards and 25,146 resolved fusion pairs. The current source suite contains 165 automated regression tests plus 13 independent Phase 4 acceptance probes; each tagged release must regenerate the guide and pass those checks before its package is published.

Original companion code is released under the [MIT License](LICENSE). Game names, card names, rules, and other third-party properties remain with their owners and are not relicensed. No ROM, BIOS, emulator, save, or copyrighted card artwork is included. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This fan project is not affiliated with or endorsed by Konami, Sony, RetroArch, Libretro, or SwanStation.

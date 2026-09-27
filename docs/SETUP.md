# YFM Fusion Companion — Windows setup

## Start the companion

1. Extract the complete ZIP to a normal folder. Do not run the executable from inside the ZIP.
2. Keep `YFM Fusion Companion.exe`, `Data`, `Artwork` and `ResearchData` together.
3. Double-click `YFM Fusion Companion.exe`.

The program is a self-contained Windows x64 application. It does not require a separate .NET installation and does not use a browser.

## Enable the read-only RetroArch connection

1. Open RetroArch.
2. Open **Settings → Network**.
3. Turn **Network Commands** on.
4. Set **Network Command Port** to `55355`.
5. Leave **Network RetroPad** and **stdin Commands** off; the companion does not use them.
6. Restart RetroArch once so the saved setting and listener are definitely active.
7. Start Yu-Gi-Oh! Forbidden Memories with the SwanStation core.
8. Open **LIVE DUEL** in the companion. It checks automatically every second; no refresh button is necessary. Green **UP TO DATE** means updates are continuing, while red **ERROR** means the latest read failed.

RetroArch may listen beyond this computer even though the companion connects only to `127.0.0.1`. Keep Windows Firewall blocking unsolicited inbound UDP port 55355 from other computers.

## Display modes

- **COMPACT LIVE** switches to a bounded 272 × 1002 pixel sidecar. Card names wrap to two lines without sideways scrolling, and 20px guardian symbols sit beneath Result. Warm glow marks selectable stars; red glow marks known opponent relationships. ATK shows effective attack after legal terminal equips, and route uses hand-slot numbers. `F(1)` through `F(5)` are monster positions; `F(6)` through `F(10)` are spell/trap positions.
- **FULL** returns to the manual, deck, live, and optimizer workspaces.
- **Always on top** keeps the companion above the game when desired.

These choices, the window position and size, and the last validated save path are remembered locally.

## Saved snapshot

Save controls are integrated into Deck Analyzer and Owned-Card Optimizer. The companion can auto-detect the SwanStation memory-card file or you can select an `.srm` or `.mcr` file. It opens the file read-only. If the game has not flushed recent progress to disk, close the game content after saving and refresh the snapshot again.

## Recommended decks and card order

Open Owned-Card Optimizer → Recommended Decks to browse six researched builds. Each icon shows required owned copies out of 40 below it. Inspect missing cards and advice, check all reference opening hands, or adapt the selected strategy to the current collection. Reference metrics may include missing cards and are not a measured win rate.

Above the suggested DECK result, Card Order defaults to Alphabetical. Card number sorts lowest first; ATK and DEF sort highest first. This remembered preference changes only the displayed order. Auto CPU settings use up to four search workers and eight analysis workers, reduced on smaller CPUs; manual choices are available.

## Diagnostics

Choose **EXPORT DIAGNOSTICS** only when you want a text report for troubleshooting. Nothing is transmitted. The report contains application and connection statuses and error messages; it excludes card, hand, deck, collection, gameplay, and emulator-memory values.

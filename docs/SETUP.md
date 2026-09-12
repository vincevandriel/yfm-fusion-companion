# YFM Fusion Companion — Windows setup

## Start the companion

1. Extract the complete ZIP to a normal folder. Do not run the executable from inside the ZIP.
2. Keep `YFM Fusion Companion.exe` and the `Data` folder together.
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
8. Open **LIVE DUEL** in the companion. It checks automatically every five seconds; no refresh button is necessary. Green **UP TO DATE** means updates are continuing, while red **ERROR** means the latest read failed.

RetroArch may listen beyond this computer even though the companion connects only to `127.0.0.1`. Keep Windows Firewall blocking unsolicited inbound UDP port 55355 from other computers.

## Display modes

- **COMPACT LIVE** switches to a 320-pixel-wide, full-work-area-height sidecar docked to the right edge. It contains only **BEST LEGAL ROUTES**, plus **PIN** and **FULL** controls. The result column shows the resulting card name, ATK shows effective attack after any legal final equip, and route uses plain hand-slot numbers. A field target is written as `F(X)`: `F(1)` through `F(5)` are the five monster slots and `F(6)` through `F(10)` are the five spell/trap slots. Compact mode has no enforced minimum window size, so it can be narrowed further manually.
- **FULL ANALYSIS** returns to the complete manual, deck, save, live, and optimizer workspaces.
- **Always on top** keeps the companion above the game when desired.

These choices, the window position and size, and the last validated save path are remembered locally.

## Saved snapshot

The companion can auto-detect the SwanStation memory-card file or you can select an `.srm` or `.mcr` file. It opens the file read-only. If the game has not flushed recent progress to disk, close the game content after saving and import the snapshot again.

## Diagnostics

Choose **EXPORT DIAGNOSTICS** only when you want a text report for troubleshooting. Nothing is transmitted. The report contains application and connection statuses and error messages; it excludes card, hand, deck, collection, gameplay, and emulator-memory values.

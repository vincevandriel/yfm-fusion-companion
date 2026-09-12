# YFM Fusion Companion — limitations and boundaries

- Live memory decoding is validated for the NTSC-U Yu-Gi-Oh! Forbidden Memories content running through RetroArch with SwanStation. Other regions, revisions, emulators, or cores are rejected or may be unavailable.
- RetroArch returns distant guest-memory regions through separate UDP reads. The companion samples the advice-relevant duel state twice and suppresses that refresh when the samples differ; it retries automatically on the next interval.
- The connection is observational. The companion cannot press game buttons, play the duel, alter cards, change Life Points, write emulator memory, configure RetroArch, or save the game.
- RetroArch Network Commands must be enabled manually. The application never edits `retroarch.cfg`.
- RetroAchievements Hardcore Mode disables save states. The companion does not use or require save states.
- Inactive field records can retain old card data. The reader filters them using the live-record active bit; it does not describe those records as a complete graveyard.
- The game retains completed-duel structures after returning to story mode. Duel-only data is displayed only while the screen-mode byte at `0x9B26C` equals the verified duel value `0xC3`; the changing action word at `0x9B23A` is not used as a screen detector.
- At the title screen before a player save is loaded, live connectivity is available but deck and collection data are correctly marked unavailable.
- A saved `.srm` or `.mcr` snapshot may lag behind current RAM until the emulator flushes it. Live state and saved snapshots are labeled separately.
- Fusion recommendations model verified Forbidden Memories fusion order and compatible terminal equips. They do not predict opponent AI, face-down opponent cards, future random draws, or undocumented effects for which the database has no rule.
- Ritual cards require their exact in-game field setup and are not treated as ordinary hand fusions. Ritual optimization is an explicit experimental profile.
- The optimizer maximizes the documented scoring and exact five-card-hand metrics; it cannot guarantee victory in a particular duel. Matchup type and field preferences should be selected for specialized battles.
- The application is English-only, offline, and Windows x64. It contains no telemetry or automatic updater.

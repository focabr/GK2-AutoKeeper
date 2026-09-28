# GK2 AutoKeeper

Automation bot for **Graveyard Keeper 2** (BepInEx 5). It performs routine chores using only actions
the player could do — no item spawning, no save editing.

> **Status: early development (0.2.x).** First routine: body processing in the morgue
> (pallet → autopsy table → extract organs → crematorium). Burial in graves is planned for 0.3.

## Features
- **Body processing** (morgue): takes bodies from the pallets, puts them on a free autopsy table, extracts organs
  (configurable), then takes the body to the crematorium and collects the result when it is done
- **F8** – turn the bot on/off (kill switch)
- **F9** – show/hide the status overlay
- **F10** – write a read-only discovery dump (JSON) of the current scene to `BepInEx/config/AutoKeeper/dumps`
- Automatically pauses in menus, pause screen, UI windows, dialogues, cutscenes and sleep
- Stops when energy is below a configurable threshold

## Requirements
- Graveyard Keeper 2 (Steam, Windows) — tested on game version **1.007**
- [BepInExPack 5.4.2305](https://thunderstore.io/c/graveyard-keeper-2/p/BepInEx/BepInExPack/) (BepInEx 5.4.23.x)

## Installation
- **Mod manager (r2modman / Thunderstore Mod Manager):** install and launch through the manager.
- **Manual:** install BepInEx 5.4.23.x next to `GraveyardKeeper2.exe`, run the game once, then copy
  `plugins/AutoKeeper/AutoKeeper.dll` to `<game>/BepInEx/plugins/AutoKeeper/`.

## Configuration
`BepInEx/config/com.focabr.gk2.autokeeper.cfg` (created on first launch): hotkeys, tick interval,
minimum energy, overlay options, verbose logging.

## Notes
- Back up your saves before using any mod: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`.
- When reporting bugs to the game developers, disable mods first (the game logs that a mod loader is present).
- Source code and issues: see the project repository. License: MIT.

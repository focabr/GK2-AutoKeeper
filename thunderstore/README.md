# GK2 AutoKeeper

Automation bot for **Graveyard Keeper 2** (BepInEx 5). It performs routine chores using only actions
the player could do — no item spawning, no save editing.

> **Status: early release (0.2.x).** First routine: body processing in the morgue
> (pallet → autopsy table → extract organs → crematorium). Burial in empty graves (destination "Grave") is in 0.3.0 and still being tested.

## Features
- **Body processing** (morgue): takes bodies from the pallets or the ground, puts them on a free autopsy table,
  extracts organs and the "Others" items (flesh, fat, blood — all configurable), then takes the body to the
  crematorium and collects the result when it is done
- **Mastery check**: extracts item by item only when your mastery chance is above the minimum you choose
- **Walks by itself** to where the work is, using the game's own doors (house → yard → morgue)
- **Eats from your hotbar** when energy is low (skips items that raise insanity)
- **Checks the crematorium first** when it arrives, and **stores what it collected in the nearest chest**
  when your inventory is nearly full (your other items are never touched)
- **F8** – turn the bot on/off (kill switch)
- **F9** – show/hide the status overlay
- **F11** – in-game settings window (also a button on the status panel); no need to edit the .cfg
- Optional: with **GK2 Mod Framework** installed, the same settings appear under **Mods** in the main and pause menus
- **F10** – write a read-only discovery dump (JSON) of the current scene to `BepInEx/config/AutoKeeper/dumps`
- Automatically pauses in menus, pause screen, UI windows, dialogues, cutscenes and sleep
- Stops when energy is below a configurable threshold and there is no food on the hotbar

## Requirements
- Graveyard Keeper 2 (Steam, Windows) — tested on game version **1.007**
- [BepInExPack 5.4.2305](https://thunderstore.io/c/graveyard-keeper-2/p/BepInEx/BepInExPack/) (BepInEx 5.4.23.x)

## Installation
- **Mod manager (r2modman / Thunderstore Mod Manager):** install and launch through the manager.
- **Manual:** install BepInEx 5.4.23.x next to `GraveyardKeeper2.exe`, run the game once, then copy
  `plugins/AutoKeeper/AutoKeeper.dll` to `<game>/BepInEx/plugins/AutoKeeper/`.

## Configuration
Use the in-game settings window (**F11**); pick the category with the ◀ ▶ arrows at the top. Values are stored in `BepInEx/config/com.focabr.gk2.autokeeper.cfg`.

## Notes
- Back up your saves before using any mod: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`.
- When reporting bugs to the game developers, disable mods first (the game logs that a mod loader is present).
- Source code and issues: see the project repository. License: MIT.

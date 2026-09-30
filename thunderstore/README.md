# GK2 AutoKeeper

Automation bot for **Graveyard Keeper 2** (BepInEx 5). It performs routine chores using only actions
the player could do — no item spawning, no save editing, no game files modified.

*Code written with Claude (Anthropic) and tested in-game by the author.*

> **Status: early release (0.3.x).** Routine: body processing in the morgue
> (pallet → autopsy table → extract organs → crematorium). Next step: burial in graves (see *Next steps*).
>
> **Bugs, help with the settings and suggestions: [open an issue on GitHub](https://github.com/focabr/GK2-AutoKeeper/issues/new/choose)**
> using a template — see *Support* below.

## Features
- **Body processing** (morgue): takes bodies from the pallets or the ground, puts them on a free autopsy table,
  extracts organs and the "Others" items (flesh, fat, blood — all configurable), then takes the body to the
  crematorium and collects the result when it is done
- **Keeps working while the crematorium burns**: autopsied bodies are parked on an empty pallet so the tables
  stay free, and go to the crematorium later
- **Fetches bodies left outside** (e.g. delivered to another area) as its last task, through the game's doors
- **Success chance check**: extracts item by item only when the game's success chance is above the minimum you choose
- **Walks by itself** to where the work is, using the game's own doors (house → yard → morgue), and works from
  the same spot the game uses (no shuffling around the table)
- **Eats from your hot bar** when energy is low, several items in a row without going over the maximum
  (skips items that raise insanity)
- **Chest**: when your inventory is nearly full, stores **only what the bot collected** in the nearest chest.
  It never takes items out of chests, and stops at once if a nearby chest loses items while it runs
- **Insanity and sleep guard**: turns itself off above a configurable insanity (default 60) and when the game's
  Lack of Sleep debuff is active (2 days awake: every energy point spent adds insanity) — or, if you choose, goes
  home, sleeps in the bed and continues the work afterwards
- Never holds the Action key while the game is aiming at a different object
- Turns itself off and forgets everything when you load a save or go back to the main menu
- **F8** – turn the bot on/off (kill switch)
- **F9** – show/hide the status panel
- **F11** – in-game settings window with the game's own look (also a button on the status panel)
- **F10** – write a read-only discovery dump (JSON) of the current scene to `BepInEx/config/AutoKeeper/dumps`
- Optional: with **GK2 Mod Framework** installed, the same settings appear under **Mods** in the main and pause menus
- Pauses automatically in menus, pause screen, UI windows, dialogues, cutscenes and sleep
- Settings window and panel in English or Brazilian Portuguese (follows the game language); log messages in Portuguese

## Requirements
- Graveyard Keeper 2 (Steam, Windows) — tested on game version **1.007.1**
- [BepInExPack 5.4.2305](https://thunderstore.io/c/graveyard-keeper-2/p/BepInEx/BepInExPack/) (BepInEx 5.4.23.x)

## Installation
- **Mod manager (r2modman / Thunderstore Mod Manager):** install and launch through the manager.
- **Manual:** install BepInEx 5.4.23.x next to `GraveyardKeeper2.exe`, run the game once, then copy the
  `plugins/AutoKeeper` folder to `<game>/BepInEx/plugins/`.

## Configuration
Use the in-game settings window (**F11**); pick the category with the ◀ ▶ arrows at the top. Values are stored in
`BepInEx/config/com.focabr.gk2.autokeeper.cfg`.

## Support
Please report bugs, ask for help with the settings or suggest features through
**[GitHub issues](https://github.com/focabr/GK2-AutoKeeper/issues/new/choose)** — pick a template (*Bug*, *Help with
settings* or *Suggestion*). Comments here cannot hold the files needed to debug.

Minimum for a bug report:
- mod version (title of the status panel / F11 window) and game version;
- what happened, what you expected and the steps to reproduce;
- `<game>/BepInEx/LogOutput.log` — **copy it before restarting the game** (the game overwrites it on every start);
- if the bot got stuck or went to the wrong place: press **F10** at that moment and attach the file from
  `BepInEx/config/AutoKeeper/dumps/` (zipped if large);
- your settings: `BepInEx/config/com.focabr.gk2.autokeeper.cfg` renamed to `.txt`.

## Next steps
- **Burial**: after the autopsy, carry the body to the graveyard, dig a grave you placed with the graveyard builder,
  bury it and close the grave with the shovel (in testing, not available in the settings yet).

## Notes
- Back up your saves before using any mod: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`.
- When reporting bugs to the game developers, disable mods first (the game logs that a mod loader is present).
- Source code: [github.com/focabr/GK2-AutoKeeper](https://github.com/focabr/GK2-AutoKeeper) · License: MIT.

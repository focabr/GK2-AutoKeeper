# GK2 AutoKeeper

**Runs your morgue hands-free.** Bodies go to the autopsy table, organs come out, the remains go to the crematorium,
and the bot eats, sleeps and stores what it collects on its own. It plays fair: only actions the player could do,
no item spawning, no save editing, no game files modified.

*Code written with Claude (Anthropic) and tested in-game by the author.*

> **Early release (0.3.x)** · tested on game version **1.008** · English and Brazilian Portuguese (follows the game
> language) · Bugs and help: **[GitHub issues](https://github.com/focabr/GK2-AutoKeeper/issues/new/choose)**

## Quick start
1. Install with **r2modman / Thunderstore Mod Manager** (or manually, see *Installation*).
2. Load your save and stand anywhere: the house, the yard or the morgue.
3. Press **F8**. The bot walks to the morgue and starts working. Press **F8** again to stop it at any time.

Adjust what it extracts, when it eats and what it does when tired in **F11**; follow what it is doing on the **F9** panel.

## What it does
- **Processes bodies:** takes them from the pallets or the ground, puts them on a free autopsy table, extracts organs
  and the "Others" items (flesh, fat, blood — all configurable), takes the body to the crematorium and collects the
  result when it is done.
- **Keeps the tables free:** while the crematorium burns, autopsied bodies wait on an empty pallet.
- **Fetches bodies left outside** (e.g. delivered to another area) through the game's doors.
- **Respects the success chance:** extracts item by item only above the minimum chance you choose.
- **Walks by itself** through the game's own doors (house → yard → morgue) and works from the same spot the game uses.

## Takes care of itself
- **Eats from your hot bar** when energy is low, several items in a row without going over the maximum (skips food
  that raises insanity).
- **Sleeps when needed** (optional): on the game's Lack of Sleep debuff, or when the food runs out, it goes home,
  sleeps in the bed and then picks up where it left off.
- **Stores what it collected** in the nearest chest when your inventory is nearly full.
- **Waits in the morgue** when there is nothing to do, ready for the next bodies.

## Safe by design
- It never takes items out of chests, and stops at once if a nearby chest loses items while it runs.
- It never holds the Action key while the game is aiming at a different object.
- It turns itself off above a configurable insanity and, by default, on the game's Lack of Sleep debuff, and tells
  you why on the panel and in the log.
- It pauses in menus, the pause screen, windows, dialogues, cutscenes and sleep, and forgets everything when you load
  a save or go back to the main menu.

## Controls
| Key | Action |
|---|---|
| **F8** | Turn the bot on/off (kill switch) |
| **F9** | Show/hide the status panel |
| **F11** | Settings window with the game's own look (also a button on the panel) |
| **F10** | Write a diagnostic file of the current scene (for bug reports) |

With **GK2 Mod Framework** installed, the same settings also appear under **Mods** in the main and pause menus.

## Requirements
- Graveyard Keeper 2 (Steam, Windows) — tested on game version **1.008**
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

## Roadmap
- **Burial**: after the autopsy, carry the body to the graveyard, dig a grave you placed with the graveyard builder,
  bury it and close the grave with the shovel (in testing, not available in the settings yet).

## Notes
- Back up your saves before using any mod: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`.
- When reporting bugs to the game developers, disable mods first (the game logs that a mod loader is present).
- Source code: [github.com/focabr/GK2-AutoKeeper](https://github.com/focabr/GK2-AutoKeeper) · License: MIT.

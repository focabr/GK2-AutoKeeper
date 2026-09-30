# GK2 AutoKeeper — texto da página do Nexus (copiar e colar)

**Nome:** GK2 AutoKeeper
**Resumo (curto):** Automation bot for the morgue: bodies → autopsy → crematorium, using only actions the player could do.
**Categoria sugerida:** Gameplay / Utilities · **Requisitos:** BepInEx 5.4.23.x

## Descrição (inglês)

**GK2 AutoKeeper** is a BepInEx 5 automation mod for **Graveyard Keeper 2**. It runs routine chores using only actions the player could do — no item spawning, no save editing, no game files modified.

**What it does (0.2.8)**
- Body processing in the morgue: pallet/ground → autopsy table → extract organs and "Others" items (flesh, fat, blood) → crematorium (or leave on the table).
- Mastery check: extracts item by item only when your success chance is above the minimum you choose.
- Walks by itself to where the work is, using the game's own doors (house → yard → morgue).
- Eats from your hotbar when energy is low (skips items that raise insanity).
- Checks the crematorium first and collects what is ready.
- Stores what it collected in the nearest chest when your inventory is nearly full (the rest of your inventory is never touched).
- Pauses automatically in menus, dialogues, cutscenes and sleep.

**Controls**
- **F8** – bot on/off (kill switch) · **F9** – status panel · **F11** – settings window (game's own look; pick the category with ◀ ▶) · **F10** – read-only diagnostic dump.

**Installation**
1. Install BepInEx 5.4.23.x (BepInExPack) next to `GraveyardKeeper2.exe` and run the game once.
2. Extract the `plugins` folder into `<game>/BepInEx/`.
3. Optional: with *GK2 Mod Framework* installed, the same settings also appear under **Mods**.

**Compatibility:** tested on game version 1.007.1. Back up your saves before using any mod. When reporting bugs to the game developers, disable mods first.

**Burial:** set *Body destination* to *Grave* and the bot walks to the graveyard, places the body in an empty grave and fills it with the shovel (needs an empty grave and a shovel on the belt).

**Uninstall:** delete `BepInEx/plugins/AutoKeeper` (and `BepInEx/config/com.focabr.gk2.autokeeper.cfg` if you want).

**Permissions:** MIT license. Source code available.

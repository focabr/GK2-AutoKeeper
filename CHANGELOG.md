# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [0.3.36] - 2026-10-03
- Status panel (F9): a "Session" line with what the bot did since the save was loaded — bodies done, times it slept and
  how long it has been on (e.g. "34 bodies · slept 19× · on for 5h02").
- Chest trips wait for the body on the table to be done and then store everything at once, instead of breaking off the
  autopsy for a single skin or bone (a 5-hour test had 50 trips for 34 bodies, 12 of them for one item). If the
  inventory is about to fill up (2 free slots or fewer) it still goes at once.
- Chests filling up: a warning when the chest just used is nearly full, and when no chest has room the turn-off
  message now says to empty a chest or build another (it used to say to turn on "Store what the bot collected", which
  was already on).
- With "Sleep, then resume", the "1.75 days without sleep" notice is a normal event, no longer a yellow warning (it is
  the expected cycle, not a problem).

## [0.3.35] - 2026-10-02
- Fixed: with two chests in the morgue, the bot went to the nearest one, which was full and only took the salt, logged
  "chest full" and went back to work carrying the rest, although the other chest had 20 free slots (it only used it
  later, after more items came in — or never, while the full chest still took one or two kinds). Now it prefers a nearby
  chest that takes everything, and if a chest fills up during a deposit it goes straight on to the next one, even when
  the inventory still has room. The message now says the rest goes to another chest; if none takes it, the log says so.
- Fixed: turned on about 2 m from a body on the ground, the bot did not walk up to it (it counted as "close enough"), the
  game had nothing in front of the player to pick up, and the bot turned off with "could not aim at the target". It now
  walks right up to items on the ground before picking them up.
- The "high insanity" turn-off message no longer suggests resting: in the game, sleeping only removes insanity when it
  cures Lack of sleep, so the message says so. It also shows one decimal (it read "60 > 60" at 60.1).

## [0.3.34] - 2026-10-02
- Status panel (F9): the event list now reads top to bottom in the order things happened (the newest is at the bottom,
  right above the button), and each line shows the game clock time it happened at instead of a running "X s ago".
  The same message several times in a row is one line with "×N" (e.g. "flesh extracted ×4"); the log file still keeps
  every line.
- Tested on game version 1.008 (the "untested game version" warning no longer shows on 1.008). The 0.3.33 fix was
  checked on 1.008: four trips to the bed in a row with no food, no frozen character and no ignored doors.
- Fixed: the low-energy message could read "10 < 10" (energy 9.6 was rounded up); it now shows the whole number below.

## [0.3.33] - 2026-10-02
- Fixed (root cause of "it stopped again" on 0.3.32): when low energy interrupted an extraction to go to sleep, the bot
  started walking while the scalpel motion was still playing. When the game ended that motion it put the character back
  on normal physics, and from then on it refused to move the character along the path (the game's own log,
  `Player.log`, shows "Trying to move non-static RB by position" about 2,700 times per stuck walk). The character stood
  at the table for 45 s, the bot gave up on that door — and it did that on every trip to the bed.
  Now the bot waits for the work motion to end before walking, and if the game freezes the character on a path anyway,
  it notices within half a second and starts the path again.
- Fixed: a door that failed was ignored for the whole session. After four of these, the only door into the house was
  ignored and the bot turned off with "no reachable home bed found". Doors are now left out for 1–2 minutes only, and
  before giving up for lack of a route the bot tries again with every door.
- Fixed: the walking time limit came from the straight-line distance. A long detour (from the mine door to the house
  the game walks through the forest, the village and the crossroads, but it is only 29 m in a straight line) ran out
  of time with the character still walking. The limit now uses the real path length the game computes, and a walk
  only counts as stuck when the character stops moving for 8 s.
- New Thunderstore icon and description.

## [0.3.32] - 2026-10-01
- Out of food with "Sleep, then resume": when energy drops below the turn-off value and there is nothing to eat on
  the hot bar, the bot finishes placing the body it carries, sleeps in the home bed (sleeping refills energy) and then
  carries on, instead of turning off. With the other choices it still turns off.
- Food warnings: a few units left, the last one used ("the hot bar food ran out"), and "nothing to eat" when energy
  is low. The turn-off reason now says why the bot could not eat (no food on the hot bar, "Eat from the hot bar" off,
  the food key did not work, or only food that raises insanity).

## [0.3.31] - 2026-10-01
- Fixed (root cause of several "strange" stops): the game renumbers its walkable regions while you play (seen in the
  F10 diagnostic files of a single session: house 766 → 765, yard 98 → 99, crematorium 1012 → 1011). The bot remembered
  the numbers of doors and objects, so after a renumbering its routes broke: "Lack of sleep, but no reachable home bed
  found", bodies outside "with no path" left on the ground, tables and crematorium "not found" right after a door.
  Region numbers are now read fresh every time; only positions are remembered.
- A door counts as crossed by distance (doors teleport far away), no longer by region number.
- The "nothing to do" log line also shows how many areas are reachable from where the bot is.

## [0.3.30] - 2026-10-01
- New option "Wait in the morgue" (`[Bodies] WaitInMorgue`, on by default): with nothing to do — for example after
  waking up at home — the bot walks to the morgue through the doors and waits there for the next bodies, instead of
  standing wherever it is. Off: it stays where it is (previous behavior).
- Diagnostics: the "nothing to do" log line also counts bodies on the ground with no known path (and their walkable
  region), and the F10 diagnostic file records the walkable region of every item on the ground.

## [0.3.29] - 2026-10-01
- Fixed: after "Sleep, then resume", the bot stayed idle in the house when the body on the table was waiting for the
  crematorium. The crematorium's centre is on a separate patch of walkable ground, so it was only found from inside the
  morgue; from the house it counted as "not found" and there was nothing to do (bodies delivered outside piled up while
  the bot slept, woke up and slept again). Objects are now located by the spot where the player stands to use them, so
  the bot walks back to the morgue after sleeping.
- The log file now says why the bot has nothing to do (tables and pallets in use, crematorium state, bodies on the
  ground here and in other areas), once per change.

## [0.3.28] - 2026-09-30
- Fixed: the description of "Process bodies" in the settings window (F11) showed gaps instead of arrows (the game's
  font has no "→"). It is now a plain sentence. "Keys 1–4" in "Eat from the hot bar" became "keys 1 to 4" for the same reason.

## [0.3.27] - 2026-09-30
- Fixed: "no path found to the target" when taking a body to the crematorium from the pallet by the stairs. The game
  hides objects that are off screen, and their work spots went with them, so the bot aimed at the middle of the
  crematorium, which cannot be walked to. Work spots of off-screen objects are now used, the last spot chosen for an
  object is remembered as a fallback, and the second retry only heads for the object itself when it is on walkable ground.

## [0.3.26] - 2026-09-30
- Fixed: the option description in the settings window (F11) could stay blank for the whole session (no text on hover).
  The game creates text materials per language and destroys them when it rescans mods (Steam Workshop scan shortly after
  startup, or Shift+F10); the description box kept a copy of a destroyed material when F11 had been opened before that.
  It now follows the font and material of a live label of the window.

## [0.3.25] - 2026-09-30
- Shorter Brazilian Portuguese labels in the settings window (F11), so every option shows at the normal font size
  (the longest ones were auto-shrunk and looked smaller and dimmer than the rest):
  "Parar com energia abaixo de", "Parar com insanidade acima de", "Ir até o trabalho pelas portas",
  "Destino após a autópsia", "Recolher o crematório primeiro", "Guardar no baú o que recolheu",
  "Espaços livres mínimos" and "Pular extrações arriscadas". Log messages that quote these options use the new names.
  English labels and all descriptions are unchanged.

## [0.3.24] - 2026-09-30
- Fixed: the option description in the settings window (F11) had no width and ran down the middle of the screen one
  letter per line. It now sits in a box under the options; long descriptions shrink to fit the box.
- The "loaded — F8 = bot, F11 = settings, F9 = status panel" message now waits until the game has loaded its language,
  so it shows in the player's language (it used to be always in English).

## [0.3.23] - 2026-09-30
- Multilingual: log messages and status panel events now follow the game language, like the settings window and the
  panel (Brazilian Portuguese when the game is in Portuguese, English otherwise). Messages written while the game is
  still starting come out in English.
- Source code comments and developer docs (this changelog, `CLAUDE.md`, `docs/`, `tools/`, `build.ps1`) are now in English.
- English players no longer see the Portuguese word "desligado" next to the bot state (fallback settings window and
  Mods menu status line) before the bot is turned on for the first time.

## [0.3.22] - 2026-09-30
- "Sleep, then resume" (Lack of sleep) validated in-game: goes through the house door to the bed, sleeps, wakes up
  without Lack of sleep and returns to the autopsy table through the basement door, continuing the body where it left off.
- Text corrected to match the game's rule: with Lack of sleep, **half** of the energy spent turns into insanity (it used
  to say "each point"). Fixed in the option's help, in the turn-off reason, in the README and in the Thunderstore README.
- Game notes (`docs/game-api-notes.md` §15): sleeping cures Lack of sleep once energy is full and removes 20 insanity;
  without it, the game does not let you sleep with full energy; waking up saves the game.

## [0.3.21] - 2026-09-30

### Changed
- **"Grave" destination removed from the options** (release build): "Body destination after autopsy" only shows
  Crematorium and Leave on table, and "Dig graves you placed" is no longer shown. The burial code stays in the mod, hidden,
  and is the announced **next step**. Anyone with `Destination = Grave` in the `.cfg` goes back to Crematorium (also when
  choosing it from the Mods menu of GK2 Mod Framework).

### Added
- **Support through GitHub**: issue templates in `.github/ISSUE_TEMPLATE/` (English + pt-BR) — *Bug / Problema*,
  *Help with settings / Ajuda na configuração* and *Suggestion / Sugestão* — asking for the minimum needed to investigate:
  versions, what happened, how to reproduce, `LogOutput.log` (copied before restarting the game), F10 diagnostic file and
  settings (`.cfg` renamed to `.txt`). Blank issues disabled.
- README, Thunderstore page and Nexus page with a **Support** section (where each file is and what to send) and
  **Next steps** (burial in a grave after the autopsy). Short description without "or a grave".

## [0.3.20] - 2026-09-30

### Changed
- **Review of all texts** (F11 window, values, status panel and messages), in English and pt-BR, with the game's official
  vocabulary and labels that say what the option does. The `.cfg` keys do not change. Main changes:
  - Lack-of-sleep option → **"When Lack of sleep hits"** (the game's effect name; pt-BR "Ao ficar com Privação de Sono");
    its "go to sleep" value → **"Sleep, then resume"**.
  - Game terms: **hot bar** ("Eat from the hot bar"; was "hotbar"); the pt-BR texts now use the game's own words for
    grave, skull and guts.
  - "Respect mastery" → **"Skip low-chance extractions"**; **"Minimum success chance (%)"**; the help text refers to the
    game's extraction window (not "Remove…", which does not exist in the pt-BR game).
  - **"Store what the bot collected"** (it used to say "when the inventory is full"); **"Go to the chest below free
    slots"**; **"Collect the crematorium first"** (was "check the crematorium first"); **"Ground body range (m)"**.
  - **"Walk to the work (through doors)"** (the idle warning already used this name); **"Dig graves you placed"**.
  - **"Eat below energy"**; **"Extract the rest of "Others""** (was "extract other items"); **"Save diagnostic file"**
    (was "save diagnostics (dump)"); **"Events in panel"** (was "log lines in panel"); **"Max time to arrive (s)"**
    (was "max walking time").
  - Status panel: idle state "ON" → **WAITING**; "In hands" → **Carrying**; "turned off by the player (hotkey)" →
    "(F8)" with the configured key.

## [0.3.19] - 2026-09-30

### Changed
- **Status panel (F9) reorganized**: title and state on the same line and, when the bot is off, paused or idle, a
  **Reason** line (before, the reason only showed up in the middle of the log). Then aligned "label: value" lines:
  **Task** (the current step), **Place** (area · game day and time), **Energy** (· insanity), **Sleep** (days without
  sleeping; highlighted near 2 days and with Lack of sleep) and **In hands** (only when carrying something). Position,
  zone, scene and money only with "Panel with technical details". Insanity used to show as "sanity".
- **Panel events**: newest on top, with "X ago" (now, 8 s, 3 min) instead of the real clock (which was confused with
  the game time), wrapped lines with indent, warnings highlighted. Technical or step-by-step messages (start/finish
  task, goal, extracting, work spots, UI, skipped mastery, dump path) only go to `LogOutput.log`.
- **Lack of sleep option** (`[Bot] OnLackOfSleep`): a single option with **Turn off the bot** (default) / **Go to sleep** /
  **Keep working**, replacing the two keys from 0.3.18 (`StopOnLackOfSleep` + `SleepWhenTired`, which overlapped). The
  old value is converted automatically and the old keys are removed from the `.cfg`.
- Shorter door message: "going through door X to <goal> (N door(s), ~M m)".

## [0.3.18] - 2026-09-30

### Added
- **Go to sleep with Lack of sleep** (`[Bot] SleepWhenTired`, **off by default**): with the Lack of sleep debuff
  (2 days without sleeping), the bot finishes delivering the body it is carrying, walks to the house bed (`bed_home`),
  presses E like the player and sleeps; when it wakes up, it continues where it left off ("Sleep: rested — back to
  work"). Takes priority over "Turn off with Lack of sleep". If it presses E 3 times and the character does not sleep,
  the bot stops with a warning.

### Fixed
- Record of what the bot collected (what goes to the chest): the game delivers the item a moment after the recipe
  finishes (it leaves the object and flies to the player), and the bot checked the inventory right away. Result in the
  0.3.16 test: ashes, salt and crematorium certificates never went to the chest, and part of the extractions did not
  either. Now each collection is counted ~2 s later (extraction: only the expected item, 1 unit; crematorium: whatever
  arrived), including when eating interrupts the work at the moment the recipe finishes.
- In-game name: the F11 window and the status panel showed "AutoKeeper"; they now show **GK2 AutoKeeper**.

## [0.3.17] - 2026-09-30

Release build: the bot code is the same as in 0.3.16.

### Changed
- Thunderstore package renamed to **`GK2_AutoKeeper`** (shows as "GK2 AutoKeeper", same as the in-game name);
  zip `GK2_AutoKeeper-x.y.z.zip`; `website_url` points to GitHub.
- New short description (Thunderstore and Nexus summary).
- GitHub README in English with a pt-BR summary, a feature table (what has already been tested in-game) and all options.
- Transparency notice: "Code written with Claude (Anthropic) and tested in-game by the author" in the README and on the
  page; Nexus page with the mandatory AI tags (AI-Generated Content + AI Media) and requirements.

## [0.3.16] - 2026-09-30

### Changed
- Chest: the "stored" log line also shows the free slots in the chest, and whatever the bot collected that **did not
  fit** (chest full, stack full or chest filter) is listed in a line "did not fit in …; stays in the inventory until a
  chest accepts it". In the 0.3.15 test the morgue chest filled up and ashes, salt, certificates, blood and a skull
  stayed in the inventory without a warning.
- Debug log: "Navigation: N useful doors" only appears when the scene or the number of doors changes (it used to repeat
  on every cache refresh).

## [0.3.15] - 2026-09-29

### Added
- **Insanity and sleep guard** (Bot tab):
  - `MaxInsanity` (default 60): the bot turns off above this value. Each insanity point takes 1 from max energy, and
    near 80 the game blocks autopsies and graves.
  - `StopOnLackOfSleep` (on): with the game's "Lack of sleep" debuff (`lack_of_sleep_debuff`, 2 days without sleeping),
    each energy point spent becomes half an insanity point; the bot turns off and asks you to sleep. At 1.75 days
    without sleep, it warns once in the log.
  - In the 0.3.12 test, insanity went from 11 to 51 in one body (max energy 89 → 49) because of this debuff.
- Dump (F10): the player's `daysWithoutSleep` and `lackOfSleep`.
- Thunderstore/Nexus package: README and Nexus page updated for 0.3.x; `manifest.json` 0.3.15.

## [0.3.14] - 2026-09-29

### Added
- **Dig placed graves** (`[Bodies] DigGraves`, on by default): with the Grave destination and no open grave
  (`grave_empty`), the bot digs with the shovel a grave the player has already placed with the graveyard builder
  (`grave_empty_place` → `grave_empty`) — only when a body is waiting for burial. It never places new graves nor
  exhumes. If it is carrying a body and the grave still needs digging, it first leaves the body on an empty pallet.
- Dump (F10): definitions with `hp`, `replaceToWgoOnDie` and `executeOnDeath`; navigation targets include placed graves
  and graves with a body.

### Fixed
- Closing the grave: the bot checked the aim before seeing whether the grave had already turned into another object;
  when the game swaps the object, the aim moves to the new one and that counted as "wrong aim" (it could end in a
  failure after 6 s).

## [0.3.13] - 2026-09-29

### Fixed
- Back and forth at the table between organs: while Action is held, the game itself takes the player to the work spot
  it chooses (`PlayerWorkComponent.FindNearestDockPoint`, with its own reach check), and that spot could be on the other
  side of the table. On the next organ the bot went back to the spot it had chosen, and the game moved it again. Now,
  when the work progresses, the bot remembers the spot where the game put it and uses that spot for the next goals on
  the same object (log: "the game works on … — using the game's spot from now on").
- `[Debug] VerboseLogging` wrote nothing to `LogOutput.log` (BepInEx's default filter drops Debug); it now writes as
  Info with the `[dbg]` prefix.

### Changed
- Food: during a stop to eat, the bot keeps eating as long as the whole food item fits in the missing energy
  (e.g. 19 → 49 → 79 of 86 with a +30 pie), instead of interrupting the work after every extraction to eat just one.
  Nothing that would go over the maximum is eaten in that sequence.
- Log: menu → Continue triggered up to three "Bot memory reset" warnings in a row (menu, `PlayerData` swap, loaded
  game). The memory is still reset in all of them, but the warning appears only once while the bot is not running.

## [0.3.12] - 2026-09-29

### Changed
- Store in the chest: after storing, the bot only goes back to the chest if the inventory fills up beyond that (before,
  it went to the chest for every item when the rest of the inventory belonged to the player and the number of free
  slots did not go up).
- Work spot log: one entry per spot (before, the "tight" spot appeared twice).

## [0.3.11] - 2026-09-29

### Fixed
- New load without closing the game (quit to the menu and "Continue"): the bot kept the memory of the previous load
  (bodies already autopsied, pallets with a parked body, items to store, graves, doors, refused chests…). Now, when
  returning to the menu or loading a game (events `MainGame.OnGoToMainMenu`/`OnGameStarted` and, as a safeguard, a
  `PlayerData` swap), the bot turns off and resets all its internal memory.

## [0.3.10] - 2026-09-29

### Added
- **Chest watch**: the bot never takes items out of chests. If a chest within 8 m loses items while the bot is on, it
  releases the keys, turns off at once and logs the goal, the step, the game's target and the position (in the 0.3.9
  test the morgue chest was emptied into the inventory again without any aim warning).

### Changed
- Work spot choice: spots squeezed between objects (another object less than 1 m away, e.g. the gap between the two
  autopsy tables) come last. The 0.3.9 "off the navmesh" test flagged precisely the side of the table where the player
  stands and is now only a tie-breaker. The game's `IsReachable` did not mark any spot as blocked.

## [0.3.9] - 2026-09-29

### Fixed
- Work spot the player cannot reach: the bot walks along a scripted path and could reach places the player cannot go
  (e.g. against/on top of the chest next to the table). It now discards spots with another object's solid collider on
  top (the game's own `DockPoint.IsReachable`) or off the navmesh, preferring a free spot.

### Added
- Log (once per object) of the work spots considered and the one chosen, to diagnose position.

## [0.3.8] - 2026-09-29

### Fixed
- **Serious:** the bot could empty the morgue chest into the inventory. After storing items, it started the extraction
  standing next to the chest (it counted as "near" the table up to 2.2 m) and held Action with the game aiming at the
  chest, which in the game means "take all". Now:
  - with a known work spot, the bot always walks to it (0.5 m tolerance);
  - it never holds Action if the game is aiming at another object; if the aim leaves the target in the middle of the
    work, it releases Action at once, repositions and, if it cannot within 6 s, stops with a warning (applies to the
    table and the grave).
- With a full inventory (no free slot nor a stack of the same item), the bot stops with a clear warning instead of
  getting stuck in the extraction.

## [0.3.7] - 2026-09-29

### Added
- **Fetch bodies from other areas** (`[Bodies] FetchRemoteBodies`, on by default): as the last task, with no body on
  the pallets, the bot goes through the doors to fetch bodies left on the ground outside (e.g. delivered by the
  Inquisition; the whole scene is known from inside the morgue) and brings them to a free table. With no free table
  and the crematorium busy, it leaves the body on an empty pallet for a later autopsy.

## [0.3.6] - 2026-09-29

### Fixed
- Store in the chest: the bot left the morgue for a distant chest (~400 m away) just because it already held bones,
  instead of using the empty chest next to the tables. Now the nearest chest that accepts the items wins; a chest that
  already holds the same items is only preferred if it is in the same area and at most 15 m farther.

## [0.3.5] - 2026-09-29

### Added
- **Park bodies on the pallet**: with the tables taken by bodies already autopsied, the crematorium busy and new bodies
  still waiting, the bot takes the body off the table, leaves it on an empty pallet and keeps doing the autopsy of the
  others. When the crematorium frees up, it fetches the parked bodies and takes them to the crematorium. When picking up
  a new body, the parked ones are left out.

### Fixed
- The bot no longer considers the task finished while the crematorium is burning and there is still a body on a table
  or pallet: it waits for the crematorium to free up.

### Changed
- Tested game version: **1.007.1** (all 388 references from the mod to the game checked; no changes needed).

## [0.3.4] - 2026-09-29

### Fixed
- "Check the crematorium first" no longer walks to an empty crematorium: the state is read from a distance and the bot only goes there when something is ready to collect (and this applies at any time, not only between one organ and the next).

## [0.3.3] - 2026-09-29

### Changed
- "Reset defaults" and "Close" sit together and centered (before, they were spread to the edges of the window).

## [0.3.2] - 2026-09-29

### Changed
- Hotkeys category: buttons of the same size and aligned, in key order (F8, F9, F10, F11); the "Start bot" button is as wide as the two actions below it.

## [0.3.1] - 2026-09-29

### Changed
- The "Organs" and "Other items" categories were merged into one: **Extraction** (mastery, organs and "Others" items).
- Window buttons: "Start bot" highlighted, with a divider above it; "Reset defaults" and "Close" side by side; help texts revised; the description of the option under the mouse now has a readable color.

## [0.3.0] - 2026-09-29

### Added
- **"Grave" destination**: the bot carries the body to a `grave_empty` (going to the graveyard through the doors),
  presses E (the game places the body and swaps the grave for `grave_body`) and closes the grave by holding Action with
  the shovel, like the player. It only closes graves in which the bot itself placed a body. With no empty grave, the
  bot stops with a warning (it does not dig on its own).
  Game definitions (dump 1.007): `grave_empty` = CustomInteraction `InsertOvrhdItem()` + `ChangeWgo("grave_body")`;
  `grave_body` = work with the shovel (Shovel).

### Removed
- Option `[Bodies] GraveCraftId` and the old attempt to bury through a recipe (the game does not use a recipe for burial).

## [0.2.8] - 2026-09-29

First release build (Thunderstore/Nexus).

### Changed
- Review of the names and organization of the settings window:
  - the "Bot" category became **General**;
  - clearer texts ("Turn off the bot below energy", "Eat when energy is below", "Cross doors to the work",
    "Process bodies", "Respect mastery", "Extract other items" etc.);
  - technical options (interval between decisions, max walking time, stop if work stalls) moved to **Advanced**;
  - "Burial recipe (id)" was removed from the window (it is still in the `.cfg`);
  - order of the Bodies options: process, destination, radius, check crematorium, chest.
- The key names in the `.cfg` did not change: settings already saved still apply (only the 3 technical options change category in the window).

## [0.2.7] - 2026-09-29

### Changed
- Settings window: the "Category" is centered and has no label, like a selector above the options.

## [0.2.6] - 2026-09-29

### Added
- **Check the crematorium first** (`[Bodies] CheckCrematoriumFirst`, on by default): when arriving in an area with a crematorium, the bot visits it before starting and collects whatever is ready.
- **Store in the chest** (`[Bodies] UseChest`, `ChestFreeSlots`): with few free slots, takes ONLY what the bot collected to the chest (extractions and crematorium; tracked by inventory difference). Prefers the chest that already holds those items; ignores quest, conveyor and garden chests.

## [0.2.5] - 2026-09-29

### Fixed
- Settings window divider: explicit height and width (before, it took up too much space).

## [0.2.4] - 2026-09-29

### Changed
- Settings window: golden divider line between the "Category" and the options, to make it clear that changing the category changes the list below.

## [0.2.3] - 2026-09-28

### Added
- **Walk to the work by itself** (`[Bot] UseDoors`, on by default): if the table/pallet/crematorium is in another area,
  the bot walks to the door (the game's `tp_*` objects) and presses E, like the player, along the shortest path
  (e.g. house → yard → morgue). The areas come from the game's own navmesh; doors that do not work are ignored for the
  rest of the session. The F10 dump gained a `navigation` section for diagnostics.
- **Eat from the hot bar** (`[Bot] AutoEat`, `EatBelowEnergy`): with low energy, presses the 1–4 key of an item that
  restores energy (the game itself consumes the item). Picks the one that wastes the least, skips items that raise
  insanity and only turns off for low energy when there is no food left.
- **"Others" items from the autopsy**: flesh, fat and blood (each one on/off) and "other items" (off), with the same
  sequence as clicking in the autopsy window.
- **Item-by-item mastery** (`RequireMastery`, `MinMasteryChance`): on top of the extraction options, skips the organ or
  item whose chance in the "Remove …" window is below the minimum (default 100% = only with full mastery).

### Changed
- Settings window: new "Organs" and "Other items" categories; "Search radius" now only applies to bodies on the ground.
- The Mods menu bridge no longer logs a red error when GK2 Mod Framework is not installed.
- Status panel: with nothing to do, it says whether it also searched behind the doors.

## [0.2.2] - 2026-09-28

### Changed
- **Settings window with the game's native look**: it is now built from pieces cloned from GK2's own Settings window
  (frame, header, "◀ value ▶" rows, sliders, buttons, fonts, colors and sounds). It is a real game window: it pauses
  the game, locks the character and closes with Esc. Categories (Bot, Bodies, Hotkeys, Panel, Advanced) in a selector
  like the game's; the option description appears on mouse hover.
  Look-capture technique inspired by GK2 Mod Framework (SuperMan4eg, MIT license).
- The old simple window (IMGUI) remains as an automatic fallback if the game changes and the native one cannot be
  built, now with the game's palette.
- **Status panel** redesigned with the game's palette (dark brown with a frame, beige labels, golden values), the
  game's font when available, more compact (technical details optional in "Detailed panel").
- New options: panel position (4 corners) and detailed panel. Log lines in the panel: default 3.

## [0.2.1] - 2026-09-28

### Added
- **In-game settings window** (F11 or the "Settings" button on the status panel): Bot, Bodies, Hotkeys, Panel and
  Advanced tabs; toggles, sliders, destination selector, key rebinding (click and press the new key), "Reset defaults",
  Start/Stop bot button. Texts in PT or EN following the game language.
  Applies immediately and saves itself to the `.cfg` (with a 1 s delay so it does not write on every slider movement).
- While the window is open, the bot pauses and the game receives no keys; clicking the status panel does not turn into
  an attack in the game.
- **Optional integration with GK2 Mod Framework** (`AutoKeeper.FrameworkBridge.dll`): the same options appear under the
  game's native "Mods" button (main menu and pause, with controller support). Without the Framework, the bridge is
  ignored.
- The status panel shows the **place** (world zone, e.g. "Yard"/"morgue"), which changes when walking/teleporting; the
  Unity "scene" almost never changes in GK2.

### Changed
- `[Bodies] ExtractOrgans` (text) became 6 on/off options: `ExtractSkin`, `ExtractBones`, `ExtractSkull`,
  `ExtractHeart`, `ExtractBrain`, `ExtractGuts`.
- `[Bot] MinEnergy` now goes from 0 to 100; `GraveCraftId` moved to the Advanced tab.

## [0.2.0] - 2026-09-28

### Added
- **First bot routine — "Process bodies"**: pallet (or ground) → free autopsy table → extracts organs
  (config `ExtractOrgans`, default `all`) → takes the body off → **crematorium** (default) → collects the result when
  ready. Destinations: `Crematorium`, `LeaveOnTable`, `Grave` (experimental).
- Virtual input (`Patches/VirtualInputPatch`, Postfix on `LazyInput.Update`): the bot presses E / holds Action through the game itself.
- Movement with the game's pathfinding (Recast graph), aiming at the target and a short adjustment step.
- Actions equivalent to the UI (extract organ, take the body off, start burial) using the windows' data classes, without opening them.
- New options: `[Bot] MoveTimeoutSeconds`, `WorkStallSeconds`; `[Bodies]` section.
- F10 dump includes `defsOfInterest` (definitions of graves, pallets, doors, crematorium).

### Changed
- Tested game version: **1.007** (1.006→1.007 diff reviewed).
- Hotkey guard (an error no longer repeats every frame).

## [0.1.0] - 2026-09-28

### Added
- BepInEx 5 plugin structure (`com.focabr.gk2.autokeeper`), Harmony with `UnpatchSelf`.
- Configuration (`.cfg`): hotkeys F8 (bot), F9 (overlay), F10 (dump), tick interval, minimum energy.
- On-screen overlay with the bot state, game version, auto-pause reason, position, energy, day/time and carried item.
- `GameApi` (single adapter) with state reading protected against game changes.
- Compatibility check against the tested game version (1.006).
- Read-only discovery dump (F10) to map the scene's objects/items/recipes.
- `BotController` with a task queue, auto-pause (menu, window, dialogue, cutscene) and stop on low energy. No tasks yet.

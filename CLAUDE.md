# GK2 AutoKeeper — boot-loader

BepInEx 5 mod (Graveyard Keeper 2, Unity 6 Mono) that automates the body routine. It only does what the player could do (no cheats, no save editing). Player-facing texts: en + pt-BR (follow the game language).

## State (2026-09-30)
- Installed version: **0.3.27** (tag v0.3.27). Game 1.007.1 validated (`Plugin.TestedGameVersion`). Package: `dist/GK2_AutoKeeper-0.3.27.zip`.
- Publishing: GitHub `focabr/GK2-AutoKeeper`, Thunderstore `focabr-GK2_AutoKeeper` (categories Mods + AI Generated), Nexus
  (tags AI-Generated Content + AI Media). Guide: Claude project "GK2" → `claude/publicacao.md`.
- Detailed state and next steps: Claude project "GK2" → `claude/status-autokeeper.md` (handoff). Do not keep volatile state here.
- Validated in-game (0.3.15): memory reset on a new load, fetching bodies outside, the game's work spot (1 adjustment
  per table), eating in sequence, parking on the pallet, sleep/insanity reading in the dump.
- Chest trips validated (0.3.15, `ChestFreeSlots` = 6): 1 trip, no repeats.
- "Sleep, then resume" validated (tested on 0.3.18; same logic up to 0.3.27): house door → bed → sleeps → cured → back to the table.
- Grave (destination `Grave` + `DigGraves`) **hidden from the UI** since 0.3.21 (`[HiddenOption]`, `BindHidden`, `KeepVisibleDestination`)
  until testing is done; it is the announced "next step". To release it: remove the attribute and switch `DigGraves` back to `Toggle`.
- Support = GitHub issues with templates in `.github/ISSUE_TEMPLATE/` (bug, help with settings, suggestion; EN + pt-BR).

## Map (Grep for these names)
- `src/AutoKeeper/Plugin.cs` — entry point, hotkeys (F8 bot, F9 panel, F10 dump, F11 settings), version.
- `Bot/Tasks/ProcessBodiesTask.cs` — routine: `Decide` (crematorium ready / chest) → `DecideCore` → Goals; `TickAim/TickWork/Tick*`.
- `Bot/Navigator.cs` — walking/doors (`Generation`). `Bot/BotController.cs` — on/off.
- `Core/GameApi*.cs` — ALL game access (World, Actions, Chest). No game logic outside it.
- `Config/Settings.cs` — options; declaration order = on-screen order; `SettingTab`.
- `UI/NativeSettingsWindow.cs` (window cloned from the game) and `UI/SettingsWindow.cs` (IMGUI, fallback).
- `src/AutoKeeper.FrameworkBridge` — optional GK2 Mod Framework bridge.
- `docs/game-api-notes.md` — game findings (section 13: grave).

## Build / delivery
- `dotnet build src/AutoKeeper/AutoKeeper.csproj -c Release "-p:GamePath=<game>"`; the bridge needs `-p:FrameworkDll=<GK2.Framework.dll>`.
- In the cloud: `apt install dotnet-sdk-8.0`, `nuget.config` with `<clear/>` (no NuGet), stage `Managed/` + `BepInEx/core`;
  bridge with `tools/FrameworkStub`; read the game code (members + IL) with `tools/Inspect` (see `tools/README.md`).
- Commits made in the cloud → device: `git bundle` → `device_commit_files` into `D:\Claude\GK2\_xfer\` →
  `git fetch <bundle> main:refs/remotes/xfer/main --tags && git merge --ff-only xfer/main`.
  In the cloud clone, run `git remote remove origin` right after cloning the bundle: with a remote, the cloud's automatic
  checker demands a push and a "Claude" signature on every commit (the real repo on the PC uses the `focabr` authorship; its `origin` is GitHub, pushed by the user — published tags are never moved).
- Delivery to the PC: copy to a NEW folder in `/mnt/user-data/outputs/` and `device_commit_files` (a reused folder delivers a stale cache). Check the version: `strings -e l AutoKeeper.dll | grep -m1 "0\.[0-9]*\.[0-9]*"`.
- The game only loads the new DLL after a restart: before analyzing a test, check `Loading [GK2 AutoKeeper x.y.z]` in `LogOutput.log`.
- Closing a version: bump `Directory.Build.props` + `Plugin.cs` + `CHANGELOG.md`, `git commit` + `git tag vX.Y.Z`; update the project handoff.
- git on the device: ask for delete permission in `D:\Claude\GK2` (otherwise `.git/*.lock` files are left behind).

## Pitfalls
- The game's layout ignores LayoutElement: use `RectTransform.sizeDelta` — for the WIDTH too (a text with width 0 wraps
  one letter per line down the screen: the F11 description box until 0.3.24).
- F11 option labels auto-shrink when too long: keep each label (pt and en) about as wide as "Ao ficar com Privação de Sono"
  (~30 chars) so it shows at the normal font size (pt labels shortened in 0.3.25; F11 checked on every tab).
- Text created by the mod must NOT keep a material copied from an inactive template: the game makes TMP materials per
  language (`TextStyle.GetMaterialFor`) and destroys them on a mods rescan (`ModsBootstrap.Tick` → `ReloadMods`, Steam
  Workshop scan after startup or Shift+F10); its labels re-apply on enable (`TextStyleComponent`). The F11 description box
  follows a live label (`SyncHintStyle`) since 0.3.26 — it was blank when F11 was opened before the rescan.
- The native window clones the game's Settings window (works in the main menu too, seen on 0.3.23); if that fails it falls back to IMGUI.
- Quest chests (customTag) and conveyor/garden chests are ignored on purpose.
- NEVER hold Action without checking the game's target (`GuardAim`): Action on a chest = take all.
- World memory (task sets, Navigator, static caches) must be reset in `ResetMemory` — a new load does not restart the plugin.
- Crematorium: state read from a distance (`GetCraftState`); only visit if `ReadyToCollect`.
- Off-screen objects: the game deactivates their view (chunk culling → `Wgo.RefreshVisuals` → `SetActive(false)`), so dock
  points are `activeInHierarchy == false`. `TryGetStandSpot` accepts `activeSelf` docks of a culled view and falls back to
  the last dock chosen (`LastDockSpots`); never aim at a crematorium's centre (isolated navmesh island). Seen 0.3.25:
  pallet by the stairs → crematorium off screen → "no path found to the target".
- Holding Action makes the GAME take the player to its own work spot (`PlayerWorkComponent.FindNearestDockPoint`, reach
  via `PlayerLocalAreaMovement.IsReachable`); the bot learns that spot (`workSpots`) — do not fight it.
- Max energy = 100 − insanity. "Lack of sleep" (`lack_of_sleep_debuff`, 2 days awake): half of the energy spent turns into
  insanity; sleeping until energy is full cures it and removes 20 insanity (game rules in `docs/game-api-notes.md` §15).
- Extracted/collected items reach the inventory a moment AFTER the recipe finishes: the bot's record (`ledger`)
  counts them through pending credits (`QueueCredit`/`SettleCredits`, ~2 s), never immediately.
- Window/panel/log texts follow the game's official vocabulary (EN: Lack of sleep, hot bar, grave, skull, guts, success
  chance; pt-BR: Privação de Sono, Barra de atalhos, Túmulo, Caveira, Entranhas, Chance de sucesso). Table and criteria:
  Claude project "GK2" → `claude/revisao-textos.md`.
- Language: code comments and dev docs in English; every player/log message goes through `Lang.T(pt, en)` (follows the game language, like the settings window and panel).
- Status panel (`UI/Overlay.cs`): technical/step-by-step messages use `ModLog.Detail` (file only); `ModLog.Info` shows up
  in the panel events — keep it short and with context.
- `LogOutput.log` drops BepInEx Debug: `ModLog.Debug` writes as Info with `[dbg]` (only with VerboseLogging).

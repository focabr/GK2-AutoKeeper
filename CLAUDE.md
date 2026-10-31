# GK2 AutoKeeper — boot-loader

BepInEx 5 mod (Graveyard Keeper 2, Unity 6 Mono) that automates the body routine. Only player actions (no cheats, no save
editing). Player/log texts: en + pt-BR via `Lang.T(pt, en)` (follows the game language); code and dev docs in English.

## State (2026-10-31)
- Current version: **0.4.1**. Needs game **1.011+** (`Plugin.MinGameVersion`; 1.011 changed `TryContinueFromQueue`); 0.3.44
  stays for 1.010 and older. Package: `dist/GK2_AutoKeeper-<version>.zip`.
- Handoff (read first): Claude project "GK2" → `claude/status-autokeeper.md`; past decisions, tests and in-game validations:
  `claude/historico-autokeeper.md`. Do not keep volatile state here.
- Publishing: GitHub `focabr/GK2-AutoKeeper`, Thunderstore `focabr-GK2_AutoKeeper` (Mods + AI Generated), Nexus (AI-Generated
  Content + AI Media), Steam Workshop item 3813049587, type **Mod**: since 1.011 the game installs, updates and removes it
  (code mod contract: `README_MODDING.txt` next to the game exe). Kit in `D:\Claude\GK2\steam-workshop\item` (`modinfo.json`
  with the version, `BepInEx/plugins/AutoKeeper/`, `_description.bbcode` and `_changenote.txt` are read by the uploader and
  not uploaded; updates keep the visibility). Guide `claude/publicacao.md`. Support = issue templates in `.github/ISSUE_TEMPLATE/`.
- Grave (`Grave` + `DigGraves`) hidden from the UI since 0.3.21 (`[HiddenOption]`, `BindHidden`, `KeepVisibleDestination`)
  until tested in game — the announced next step. To release: remove the attribute, switch `DigGraves` back to `Toggle`.

## Map (Grep for these names)
- `src/AutoKeeper/Plugin.cs` — entry point, hotkeys (F8 bot, F9 panel, F10 dump, F11 settings), version.
- `Bot/Tasks/ProcessBodiesTask.cs` — routine: `Decide` (crematorium ready / chest) → `DecideCore` → Goals; `TickAim/TickWork/Tick*`.
- `Bot/Navigator.cs` — walking/doors (`Generation`). `Bot/BotController.cs` — on/off, eating, sleep. `Bot/SessionStats.cs` — panel summary.
- `Core/GameApi*.cs` — ALL game access (World, Actions, Chest). No game logic outside it.
- `Config/Settings.cs` — options; declaration order = on-screen order; `SettingTab`.
- `UI/Overlay.cs` (F9 panel), `UI/NativeSettingsWindow.cs` (F11 cloned from the game) and `UI/SettingsWindow.cs` (IMGUI fallback).
- `src/AutoKeeper.FrameworkBridge` — optional GK2 Mod Framework bridge.
- `docs/game-api-notes.md` — game findings (§13 grave, §15 sleep/insanity, §16 movement/physics).
- `docs/pitfalls.md` — every pitfall and the full delivery details.

## Build / delivery
- Build: `dotnet build src/AutoKeeper/AutoKeeper.csproj -c Release "-p:GamePath=<game>"`; bridge with
  `-p:FrameworkDll=<tools/FrameworkStub build>`. Cloud: dotnet-sdk-8.0, `nuget.config` with `<clear/>`, stage the `Managed/`
  DLLs the csproj names + `BepInEx/core`; read the game code with `tools/Inspect`. If nuget.org is blocked (proxy 403):
  build a temporary copy of the csproj without the `BepInEx.Core` / `UnityEngine.Modules` PackageReferences, with
  `-p:AssemblyName=AutoKeeper --source <empty dir>` (the game DLLs via HintPath are enough; compare the assembly references
  with the previous DLL). Do not commit that copy.
- Deliver from a NEW folder in `/mnt/user-data/outputs/` with `device_commit_files`; check `strings -e l AutoKeeper.dll`.
  ALWAYS install the plugin yourself (standing request). With the game open Windows locks the loaded DLL (copy fails with
  "Invalid argument"): rename `AutoKeeper.dll` to `AutoKeeper.dll.old` and copy the new one (what `build.ps1` does; the
  `.old` stays until deletion is allowed — BepInEx ignores it). Verify the version string in the installed DLL. The game
  loads it on restart — check `Loading [GK2 AutoKeeper x.y.z]` before analyzing a test.
- End of EVERY version: bump `Directory.Build.props` + `Plugin.cs` + `CHANGELOG.md` (+ manifest, README, nexus page) →
  commit + tag (author focabr <focabr@gmail.com>, no trailers, unsigned) → install → copy the DLL to the Workshop kit and
  bump `modinfo.json` / `_changenote.txt`. Published tags never move (a fix = a new version); a tag that was never pushed
  may be redone.
- Publishing (user's project instructions): GitHub is the source — clone it, do not use the PC copy. Only after the user
  says they tested in game: `add_repo(focabr, GK2-AutoKeeper, push)` → push branch + tag → `gh release create` with the zip
  and the CHANGELOG section. Then give the user the cmd line to update the PC:
  `cd /d D:\Claude\GK2\GK2-AutoKeeper && git pull --tags`. Workshop (Shift+F11 in game), Thunderstore and Nexus stay
  manual (user). Commands for the user are always cmd lines starting with `cd /d`.

## Top pitfalls (details in `docs/pitfalls.md`)
- Never start a walk while the player is in `WorkPlayerState` (frozen body: "Trying to move non-static RB by position").
- Never cache navmesh region numbers (the game renumbers them); door bans are temporary; walk limits use the real path.
- Never hold Action without `GuardAim` (Action on a chest = take all). Ground items need the player ~0.6 m away, facing them.
- Zombie workers take materials from chests near their stations: the chest watch stops the bot only when items go from a
  chest INTO the player's inventory (`WatchChests`), never on a chest losing items by itself.
- A free crematorium/grave is fed before any extraction (`DecideCore` step 3); keep it that way (cremation runs on its own).
- World memory must be reset in `ResetMemory` / `ResetForNewWorld` — a new load does not restart the plugin.
- F11 texts: no "→" or "–" (missing glyphs); labels ~30 chars; mod TMP text follows a live label (`SyncHintStyle`).
- Panel messages re-translate on a language switch only when the logged string is the result of ONE `Lang.T` call
  (`Lang.TryPair`); pass `Lang.T(...)` straight to `ModLog`/`Stop`, do not glue translated pieces together.
- Sleep only removes insanity (−20) when it cures Lack of sleep; max energy = 100 − insanity.
- For "it stopped/froze" reports read `Player.log` too (`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2`).

# GK2 AutoKeeper — boot-loader

BepInEx 5 mod (Graveyard Keeper 2, Unity 6 Mono) that automates the body routine. Only player actions (no cheats, no save
editing). Player/log texts: en + pt-BR via `Lang.T(pt, en)` (follows the game language); code and dev docs in English.

## State (2026-10-04)
- Current version: **0.3.39**. Game 1.008 validated (`Plugin.TestedGameVersion`). Package: `dist/GK2_AutoKeeper-<version>.zip`.
- Handoff (read first): Claude project "GK2" → `claude/status-autokeeper.md`; past decisions, tests and in-game validations:
  `claude/historico-autokeeper.md`. Do not keep volatile state here.
- Publishing: GitHub `focabr/GK2-AutoKeeper`, Thunderstore `focabr-GK2_AutoKeeper` (Mods + AI Generated), Nexus (AI-Generated
  Content + AI Media), Steam Workshop showcase item 3813049587 (type "Translation"; subscribing does not install; kit in
  `D:\Claude\GK2\steam-workshop\item`); guide `claude/publicacao.md`. Support = issue templates in `.github/ISSUE_TEMPLATE/`.
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
  `-p:FrameworkDll=<tools/FrameworkStub build>`. Cloud: dotnet-sdk-8.0, `nuget.config` with `<clear/>`, stage `Managed/` +
  `BepInEx/core`; read the game code with `tools/Inspect`.
- Deliver from a NEW folder in `/mnt/user-data/outputs/` with `device_commit_files`; check `strings -e l AutoKeeper.dll`.
  ALWAYS install the plugin yourself (standing request): `build.ps1` fails while the game runs ("DLL in use"), so `mv` the
  loaded `AutoKeeper.dll` to `.dll.old` (rename works), `cp` `src/AutoKeeper/bin/Release/AutoKeeper.dll` in, delete the `.old`,
  and verify the version string. The game loads it on restart — check
  `Loading [GK2 AutoKeeper x.y.z]` before analyzing a test.
- ALWAYS end the reply with ONE code block holding the full, ready-to-run cmd lines (`cd /d` first, chained with `&&`): `git add`,
  `commit`, `tag`, `push` (branch + tags) and `gh release create` — even when the commit/tag were already made locally (user's standing request).
- End of EVERY version (user's standing request): bump `Directory.Build.props` + `Plugin.cs` + `CHANGELOG.md` (+ manifest,
  README, nexus page) → local commit + tag (author focabr, no trailers, unsigned) → install → send the cmd lines (`cd /d`
  first, chained with `&&`) to push and `gh release create`. Without `device_bash`, writes into `.git` are refused and
  terminals are click-only: the commit + tag go as the first command line (new files need `git add`). Published tags never move.
  Also copy the DLLs to `D:\Claude\GK2\steam-workshop\item\BepInEx\plugins\AutoKeeper\` and remind the user: in game
  Shift+F11 → item → Upload, then re-paste the description and set Public (the game's uploader resets both every time).

## Top pitfalls (details in `docs/pitfalls.md`)
- Never start a walk while the player is in `WorkPlayerState` (frozen body: "Trying to move non-static RB by position").
- Never cache navmesh region numbers (the game renumbers them); door bans are temporary; walk limits use the real path.
- Never hold Action without `GuardAim` (Action on a chest = take all). Ground items need the player ~0.6 m away, facing them.
- World memory must be reset in `ResetMemory` / `ResetForNewWorld` — a new load does not restart the plugin.
- F11 texts: no "→" or "–" (missing glyphs); labels ~30 chars; mod TMP text follows a live label (`SyncHintStyle`).
- Panel messages re-translate on a language switch only when the logged string is the result of ONE `Lang.T` call
  (`Lang.TryPair`); pass `Lang.T(...)` straight to `ModLog`/`Stop`, do not glue translated pieces together.
- Sleep only removes insanity (−20) when it cures Lack of sleep; max energy = 100 − insanity.
- For "it stopped/froze" reports read `Player.log` too (`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2`).

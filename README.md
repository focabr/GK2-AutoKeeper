# GK2 AutoKeeper

Automation bot for **Graveyard Keeper 2** (BepInEx 5 + HarmonyX, Unity 6 Mono). It runs the morgue routine —
bodies → autopsy table → organs → crematorium — using **only actions the player could do**: no item spawning,
no save editing, no game files modified.

> Code written with Claude (Anthropic) and tested in-game by the author. Published with the "AI Generated" category on
> Thunderstore and the matching Generative AI tags on Nexus Mods.

- Game: Graveyard Keeper 2 **1.008** (Steam, Windows) · BepInEx **5.4.23.x** (BepInExPack 5.4.2305)
- Plugin GUID: `com.focabr.gk2.autokeeper` · Version: see `Directory.Build.props` / [CHANGELOG](CHANGELOG.md)
- Downloads: Thunderstore (`focabr-GK2_AutoKeeper`) · Nexus Mods · [Releases](../../releases)
- **Bugs, help and suggestions: [open an issue](https://github.com/focabr/GK2-AutoKeeper/issues/new/choose) with a template** — see [Support](#support--suporte)

*Resumo em português no fim da página.*

## Features
| Feature | Since | Tested in-game |
|---|---|---|
| Body processing: pallet/ground → free autopsy table → organs and "Others" (flesh, fat, blood) → crematorium | 0.2.0 | ✅ |
| Mastery check: extracts item by item only above the minimum success chance you choose | 0.2.3 | ✅ |
| Walks to the work by itself through the game's doors (house → yard → morgue) | 0.2.3 | ✅ |
| Eats from the hotbar when energy is low, several items in a row without going over the max; skips insanity food | 0.2.3 / 0.3.13 | ✅ |
| Checks the crematorium on arrival and collects what is ready | 0.2.6 / 0.3.4 | ✅ |
| Parks autopsied bodies on an empty pallet while the crematorium burns | 0.3.5 | ✅ |
| Fetches bodies left in other areas (e.g. outside the morgue) as its last task | 0.3.7 | ✅ |
| With nothing to do (e.g. after sleeping at home), walks back to the morgue and waits there | 0.3.30 | ✅ |
| Stores **only what it collected** in the nearest chest with room when the inventory is nearly full — between bodies, not mid-autopsy; if a chest fills up, the rest goes to the next one | 0.2.6 / 0.3.12 / 0.3.35 / 0.3.36 | ✅ |
| Safety: never holds Action while the game aims at another object; stops if a nearby chest loses items | 0.3.8 / 0.3.10 | ✅ |
| Works from the spot the game itself uses (no shuffling around the table) | 0.3.13 | ✅ |
| Resets its memory when you load a save or return to the menu | 0.3.11 | ✅ |
| Turns off above a configurable insanity and on the game's Lack of Sleep debuff | 0.3.15 | ✅ |
| On Lack of Sleep you choose: turn off (default), go home and sleep in the bed then resume, or keep working | 0.3.18 / 0.3.19 | ✅ |
| Out of food with "Sleep, then resume": sleeps in the bed to recover energy instead of turning off; warns when the food runs out | 0.3.32 / 0.3.33 | ✅ |
| Status panel in aligned blocks: state and reason, task, place/time, energy/insanity, sleep, a one-line session summary (bodies done, sleeps, time on), latest events in order with the game time (repeats as ×N) | 0.3.19 / 0.3.34 / 0.3.36 | ✅ |
| Status panel with the game's own look: frame and title plate of the game's windows, the game's font, sections (Status, Session, Events) and game buttons (bot on/off, settings, diagnostic, close); follows a language switch | 0.3.38 | ✅ |

⏳ = fixed in the latest version, waiting for an in-game test.

The bot pauses by itself in menus, pause, UI windows, dialogues, cutscenes and sleep, and stops with the reason shown
on the status panel and in the log (low energy without food, work not progressing, target unreachable…).

## Install
- **Mod manager (r2modman / Thunderstore Mod Manager):** install `GK2_AutoKeeper` and launch through the manager.
- **Manual:** install BepInEx 5.4.23.x next to `GraveyardKeeper2.exe`, run the game once, then copy the
  `plugins/AutoKeeper` folder from the release zip to `<game>/BepInEx/plugins/`.
- Optional: with **GK2 Mod Framework** ([Nexus](https://www.nexusmods.com/graveyardkeeper2/mods/42)) the same settings
  also appear under **Mods** in the main and pause menus.
- Back up your saves first: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`.
- Uninstall: delete `BepInEx/plugins/AutoKeeper` (and `BepInEx/config/com.focabr.gk2.autokeeper.cfg` if you want).

## Support / Suporte
**Bugs, help with the settings and suggestions go through GitHub issues — pick a template:**
**[Open an issue](https://github.com/focabr/GK2-AutoKeeper/issues/new/choose)**. Comments on Thunderstore or Nexus are
hard to follow and cannot hold the files needed to debug, so please use the issue templates.

| Template | Use it when | Minimum to send |
|---|---|---|
| **Bug / Problema** | the bot did something wrong, got stuck or stopped | mod and game version · what happened and what you expected · steps to reproduce · `LogOutput.log` · F10 diagnostic if it got stuck · your settings |
| **Help with settings / Ajuda na configuração** | you are not sure which options to use | mod version · what you want the bot to do · what happens instead · your settings (file or F11 screenshots) |
| **Suggestion / Sugestão** | new routine or improvement | the routine · how you do it by hand today · how the bot should behave · what must never happen |

Where the files are (`<game>` = the Graveyard Keeper 2 folder):
- **Log:** `<game>/BepInEx/LogOutput.log` — **copy it before restarting the game**; the game overwrites it on every start.
  For more detail, turn on F11 → Advanced → *Verbose logging* and reproduce the problem.
- **F10 diagnostic:** press **F10** at the moment of the problem → `<game>/BepInEx/config/AutoKeeper/dumps/*.json`
  (read-only snapshot of the scene; zip it if it is large).
- **Settings:** `<game>/BepInEx/config/com.focabr.gk2.autokeeper.cfg` — GitHub does not accept `.cfg`, rename it to `.txt`.
- **Mod version:** title of the status panel (F9) or of the F11 window, e.g. *GK2 AutoKeeper 0.3.38*.

## Roadmap / Próximos passos
- **Burial:** after the autopsy, carry the body to the graveyard, dig a grave you placed with the graveyard builder, bury the
  body and close the grave with the shovel. The code exists and is hidden from the settings until the in-game tests are done.
  / **Enterrar:** depois da autópsia, levar o corpo ao cemitério, cavar um túmulo que você marcou com o construtor, enterrar e
  fechar com a pá. O código existe e fica escondido das opções até terminarem os testes no jogo.

## Keys (configurable)
| Key | Action |
|---|---|
| F8 | bot on/off (kill switch) |
| F9 | show/hide the status panel |
| F10 | read-only diagnostic dump (JSON) to `BepInEx/config/AutoKeeper/dumps/` |
| F11 | settings window (game's own look; categories with ◀ ▶) |

## Settings (F11)
All options are saved automatically to `BepInEx/config/com.focabr.gk2.autokeeper.cfg`; there is no need to edit it.
Settings window, status panel and log messages in English or Brazilian Portuguese (follows the game language).

| Category | Option (`[Section] Key`) | Default | What it does |
|---|---|---|---|
| General | `[Bot] AutoEat` | on | eat an energy item from the hotbar (keys 1–4) when energy is low |
| General | `[Bot] EatBelowEnergy` | 20 | energy that triggers eating |
| General | `[Bot] MinEnergy` | 10 | turn off below this energy (when there is no food) |
| General | `[Bot] MaxInsanity` | 60 | turn off above this insanity (each point lowers max energy by 1; near 80 the game blocks autopsy) |
| General | `[Bot] OnLackOfSleep` | Stop | when the game's Lack of sleep hits (2 days awake: half of the energy spent becomes insanity): `Stop` the bot, `Sleep` (go home, sleep in the bed, then resume) or `KeepWorking` |
| General | `[Bot] UseDoors` | on | go through doors along the shortest route to the work |
| Bodies | `[Bodies] Enabled` | on | body routine on/off |
| Bodies | `[Bodies] Destination` | Crematorium | `Crematorium` or `LeaveOnTable` (burial is on the roadmap) |
| Bodies | `[Bodies] SearchRadius` | 80 | range for loose bodies on the ground |
| Bodies | `[Bodies] FetchRemoteBodies` | on | fetch bodies left in other areas as the last task |
| Bodies | `[Bodies] WaitInMorgue` | on | with nothing to do, walk to the morgue and wait there |
| Bodies | `[Bodies] CheckCrematoriumFirst` | on | collect finished crematorium results before starting |
| Bodies | `[Bodies] UseChest` | on | store only what the bot collected in the nearest chest |
| Bodies | `[Bodies] ChestFreeSlots` | 3 | go to the chest below this many free inventory slots |
| Extraction | `[Bodies] RequireMastery` / `MinMasteryChance` | on / 100 | extract only above the chosen success chance |
| Extraction | `[Bodies] ExtractSkin` … `ExtractGuts` | on | which organs to extract |
| Extraction | `[Bodies] ExtractFlesh` / `ExtractFat` / `ExtractBlood` | on | "Others" items |
| Extraction | `[Bodies] ExtractOtherPocket` | off | any other "Others" item |
| Hotkeys | `[Hotkeys] ToggleBot` / `ToggleOverlay` / `DiscoveryDump` / `OpenSettings` | F8 / F9 / F10 / F11 | keys |
| Panel | `[Overlay] ShowOverlay` / `GameLook` / `GameLookSize` / `Position` / `Detailed` / `LogLines` | on / on / Small / top-left / off / 3 | status panel (`GameLook` off = simple panel; `GameLookSize` Large = the game's window size) |
| Advanced | `[Debug] VerboseLogging` | off | debug lines (`[dbg]`) in `BepInEx/LogOutput.log` |
| Advanced | `[Bot] TickIntervalSeconds` / `MoveTimeoutSeconds` / `WorkStallSeconds` | 0.25 / 45 / 20 | timing and give-up limits |

## Build from source
Requirements: .NET SDK 8+, the game with BepInEx 5.4.23.x installed. Game and BepInEx DLLs are referenced from **your**
installation (`Private=false`) and never committed or redistributed (see `lib/README.md`).

1. Copy `GamePath.props.example` to `GamePath.props` and set your game path (or set `GK2_GAME_PATH`).
2. In PowerShell, in the project folder:
   ```powershell
   .\build.ps1            # Release build + install to <game>\BepInEx\plugins\AutoKeeper\
   .\build.ps1 -Package   # also creates dist\GK2_AutoKeeper-x.y.z.zip (Thunderstore format)
   ```
   Or just `dotnet build src/AutoKeeper -c Release`. If PowerShell blocks the script:
   `powershell -ExecutionPolicy Bypass -File .\build.ps1`.

## Project layout
```
src/AutoKeeper/
  Plugin.cs            BepInEx entry: config, Harmony, hotkeys, lifecycle
  Config/Settings.cs   every ConfigEntry (declaration order = order on screen)
  Core/GameApi*.cs     the ONLY code that touches game classes (adapter)
  Core/StateReader.cs  snapshot for the panel and the bot
  Bot/                 state machine, doors/navigation, tasks (Tasks/ProcessBodiesTask.cs)
  Patches/             isolated Harmony patches (virtual input)
  UI/                  status panel, native settings window, IMGUI fallback
src/AutoKeeper.FrameworkBridge/  optional bridge to the GK2 Mod Framework "Mods" menu
docs/                  reverse-engineering notes (pt-BR) and store page text
thunderstore/          manifest.json, icon.png, README.md of the package
tools/                 dev-only: game metadata/IL inspector and a Framework stub for builds
.github/ISSUE_TEMPLATE/ issue forms: bug, help with settings, suggestion (EN + pt-BR)
build.ps1              build + deploy + release zip
```

## Good practices followed
- No changes to game files on disk: runtime Harmony patches only (Harmony ID = GUID).
- Game DLLs are compile-time references only; nothing from the game is redistributed.
- The bot acts in ticks, pauses on any menu/window/dialogue, and logs a warning if the game version differs from the tested one.
- When reporting bugs to the game developers, disable mods first (the game logs that a mod loader is present).

## License
MIT — see [LICENSE](LICENSE).

---

## Resumo (pt-BR)
**GK2 AutoKeeper** é um bot para o **Graveyard Keeper 2** que faz a rotina do necrotério (palete → mesa de autópsia →
órgãos → crematório) usando só ações que o jogador faria: sem criar itens, sem editar o save. **F8** liga/desliga,
**F9** painel, **F11** configurações (tela, painel e mensagens do log em português quando o jogo está em português), **F10** diagnóstico.
Instalação: BepInEx 5.4.23.x no jogo e a pasta `plugins/AutoKeeper` do zip em `<jogo>/BepInEx/plugins/`.
Código escrito com o Claude (Anthropic) e testado no jogo pelo autor. Notas de engenharia reversa em `docs/game-api-notes.md`.
**Suporte:** problemas, ajuda na configuração e sugestões por **[issue no GitHub](https://github.com/focabr/GK2-AutoKeeper/issues/new/choose)**,
escolhendo o modelo. Mínimo para um problema: versão do mod e do jogo, o que aconteceu e como reproduzir, o `LogOutput.log`
(copiado **antes** de reiniciar o jogo), o diagnóstico **F10** se o bot travou e o arquivo de configurações renomeado para `.txt`.
**Próximo passo:** enterrar os corpos no túmulo depois da autópsia.

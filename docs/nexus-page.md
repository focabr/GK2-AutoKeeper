# GK2 AutoKeeper — página do Nexus (copiar e colar)

**Nome do mod:** GK2 AutoKeeper
**Versão:** 0.3.19 · **Arquivo principal:** `GK2_AutoKeeper-0.3.19.zip` (o mesmo zip do Thunderstore)
**Resumo (campo curto):** Automation bot for the morgue: carries bodies to the autopsy table, extracts organs and sends them to the crematorium or a grave, using only actions the player could do. F8 on/off, F11 settings.
**Categoria sugerida:** Gameplay (ou Utilities, se existir) · **Idioma:** English (UI também em pt-BR)
**Requisitos (aba Requirements):** BepInEx for Graveyard Keeper 2 — https://www.nexusmods.com/graveyardkeeper2/mods/48
**Opcional (mencionar na descrição):** GK2 Mod Framework — https://www.nexusmods.com/graveyardkeeper2/mods/42
**Generative AI Usage (obrigatório desde 29/07/2026):** marcar **AI-Generated Content** (código escrito com IA) e
**AI Media** (texto desta página escrito com IA). Não usar "AI Assisted".
**Permissões:** MIT (código aberto) · **Fonte:** https://github.com/focabr/GK2-AutoKeeper

## Descrição (inglês)

**GK2 AutoKeeper** is a BepInEx 5 automation mod for **Graveyard Keeper 2**. It runs the morgue routine using only actions the player could do — no item spawning, no save editing, no game files modified.

*Code written with Claude (Anthropic) and tested in-game by the author.*

**What it does (0.3.19)**
- Body processing in the morgue: pallet/ground → autopsy table → extract organs and "Others" items (flesh, fat, blood) → crematorium, grave or leave on the table.
- Keeps working while the crematorium burns: autopsied bodies are parked on an empty pallet and cremated later.
- Fetches bodies left in other areas (e.g. outside the morgue) as its last task, through the game's doors.
- Mastery check: extracts item by item only when your success chance is above the minimum you choose.
- Walks by itself to where the work is and works from the same spot the game uses.
- Eats from your hotbar when energy is low, several items in a row without going over the maximum (skips items that raise insanity).
- Stores only what it collected in the nearest chest when your inventory is nearly full. It never takes items out of chests and stops at once if a nearby chest loses items while it runs.
- Insanity and sleep guard: turns itself off above a configurable insanity (default 60) and when the game's Lack of Sleep debuff is active. Or, if you choose: go home, sleep in the bed and continue the work afterwards.
- Grave destination (experimental): digs a grave you marked with the graveyard builder, places the body and fills it with the shovel. It never marks new graves and never exhumes.
- Pauses automatically in menus, dialogues, cutscenes and sleep; turns itself off and forgets everything when you load a save.
- Settings window and panel in English or Brazilian Portuguese (follows the game language); log messages in Portuguese.

**Controls**
- **F8** – bot on/off (kill switch) · **F9** – status panel · **F11** – settings window (game's own look; pick the category with ◀ ▶) · **F10** – read-only diagnostic dump.

**Installation**
1. Install BepInEx 5.4.23.x (BepInExPack) next to `GraveyardKeeper2.exe` and run the game once.
2. Copy the `plugins/AutoKeeper` folder from the zip into `<game>/BepInEx/plugins/`.
3. Optional: with *GK2 Mod Framework* installed, the same settings also appear under **Mods**.

**Compatibility:** tested on game version 1.007.1. Back up your saves before using any mod. When reporting bugs to the game developers, disable mods first. For bug reports here, attach `BepInEx/LogOutput.log`.

**Uninstall:** delete `BepInEx/plugins/AutoKeeper` (and `BepInEx/config/com.focabr.gk2.autokeeper.cfg` if you want).

**Source code and license:** https://github.com/focabr/GK2-AutoKeeper — MIT.

## Descrição (pt-BR, opcional)

**GK2 AutoKeeper** automatiza a rotina do necrotério no **Graveyard Keeper 2** usando só ações que o jogador faria (sem criar itens, sem editar o save). Palete/chão → mesa de autópsia → extrai órgãos e "Outros" → crematório, cova ou deixa na mesa; estaciona corpos no palete com o crematório ocupado; busca corpos largados em outras áreas; come da barra rápida; desliga com insanidade alta ou falta de sono; guarda no baú só o que recolheu; cava covas marcadas pelo construtor (experimental). **F8** liga/desliga, **F9** painel, **F11** configurações, **F10** dump de diagnóstico. Código escrito com o Claude (Anthropic) e testado no jogo pelo autor.

# GK2 AutoKeeper — página do Nexus (copiar e colar)

**Nome do mod:** GK2 AutoKeeper
**Versão:** 0.3.22 · **Arquivo principal:** `GK2_AutoKeeper-0.3.22.zip` (o mesmo zip do Thunderstore)
**Resumo (campo curto):** Automation bot for the morgue: carries bodies to the autopsy table, extracts organs and sends them to the crematorium, using only actions the player could do. F8 on/off, F11 settings.
**Categoria sugerida:** Gameplay (ou Utilities, se existir) · **Idioma:** English (UI também em pt-BR)
**Requisitos (aba Requirements):** BepInEx for Graveyard Keeper 2 — https://www.nexusmods.com/graveyardkeeper2/mods/48
**Opcional (mencionar na descrição):** GK2 Mod Framework — https://www.nexusmods.com/graveyardkeeper2/mods/42
**Generative AI Usage (obrigatório desde 29/07/2026):** marcar **AI-Generated Content** (código escrito com IA) e
**AI Media** (texto desta página escrito com IA). Não usar "AI Assisted".
**Permissões:** MIT (código aberto) · **Fonte:** https://github.com/focabr/GK2-AutoKeeper
**Aba Bugs/Posts:** se o Nexus deixar, desligar a aba *Bugs* e fixar um post apontando para as issues do GitHub.

## Descrição (inglês)

**GK2 AutoKeeper** is a BepInEx 5 automation mod for **Graveyard Keeper 2**. It runs the morgue routine using only actions the player could do — no item spawning, no save editing, no game files modified.

*Code written with Claude (Anthropic) and tested in-game by the author.*

**Bugs, help with the settings and suggestions:** please open an issue on GitHub using a template — https://github.com/focabr/GK2-AutoKeeper/issues/new/choose (details under *Support* below).

**What it does (0.3.22)**
- Body processing in the morgue: pallet/ground → autopsy table → extract organs and "Others" items (flesh, fat, blood) → crematorium (or leave on the table).
- Keeps working while the crematorium burns: autopsied bodies are parked on an empty pallet and cremated later.
- Fetches bodies left in other areas (e.g. outside the morgue) as its last task, through the game's doors.
- Success chance check: extracts item by item only when the game's success chance is above the minimum you choose.
- Walks by itself to where the work is and works from the same spot the game uses.
- Eats from your hot bar when energy is low, several items in a row without going over the maximum (skips items that raise insanity).
- Stores only what it collected in the nearest chest when your inventory is nearly full. It never takes items out of chests and stops at once if a nearby chest loses items while it runs.
- Insanity and sleep guard: turns itself off above a configurable insanity (default 60) and when the game's Lack of sleep hits. Or, if you choose: go home, sleep in the bed and continue the work afterwards.
- Pauses automatically in menus, dialogues, cutscenes and sleep; turns itself off and forgets everything when you load a save.
- Settings window and panel in English or Brazilian Portuguese (follows the game language); log messages in Portuguese.

**Controls**
- **F8** – bot on/off (instant stop) · **F9** – status panel · **F11** – settings window (game's own look; pick the category with ◀ ▶) · **F10** – read-only diagnostic file.

**Installation**
1. Install BepInEx 5.4.23.x (BepInExPack) next to `GraveyardKeeper2.exe` and run the game once.
2. Copy the `plugins/AutoKeeper` folder from the zip into `<game>/BepInEx/plugins/`.
3. Optional: with *GK2 Mod Framework* installed, the same settings also appear under **Mods**.

**Support**
Bug reports, help with the settings and suggestions go through GitHub issues (templates *Bug*, *Help with settings*, *Suggestion*): https://github.com/focabr/GK2-AutoKeeper/issues/new/choose
Minimum for a bug report:
- mod version (title of the status panel / F11 window) and game version;
- what happened, what you expected and the steps to reproduce;
- `<game>/BepInEx/LogOutput.log` — copy it **before restarting the game** (the game overwrites it on every start);
- if the bot got stuck or went to the wrong place: press **F10** at that moment and attach the file from `BepInEx/config/AutoKeeper/dumps/` (zipped if large);
- your settings: `BepInEx/config/com.focabr.gk2.autokeeper.cfg` renamed to `.txt`.

**Next steps**
- Burial: after the autopsy, carry the body to the graveyard, dig a grave you placed with the graveyard builder, bury it and close the grave with the shovel (in testing).

**Compatibility:** tested on game version 1.007.1. Back up your saves before using any mod. When reporting bugs to the game developers, disable mods first.

**Uninstall:** delete `BepInEx/plugins/AutoKeeper` (and `BepInEx/config/com.focabr.gk2.autokeeper.cfg` if you want).

**Source code and license:** https://github.com/focabr/GK2-AutoKeeper — MIT.

## Descrição (pt-BR, opcional)

**GK2 AutoKeeper** automatiza a rotina do necrotério no **Graveyard Keeper 2** usando só ações que o jogador faria (sem criar itens, sem editar o save). Palete/chão → mesa de autópsia → extrai órgãos e "Outros" → crematório (ou deixa na mesa); estaciona corpos no palete com o crematório ocupado; busca corpos largados em outras áreas; come da barra de atalhos; desliga com insanidade alta ou Privação de Sono (ou, se você escolher, vai dormir e continua); guarda no baú só o que recolheu. **F8** liga/desliga, **F9** painel, **F11** configurações, **F10** arquivo de diagnóstico. Código escrito com o Claude (Anthropic) e testado no jogo pelo autor.

**Suporte:** problemas, ajuda na configuração e sugestões por issue no GitHub, escolhendo o modelo: https://github.com/focabr/GK2-AutoKeeper/issues/new/choose — envie a versão do mod e do jogo, o que aconteceu e como reproduzir, o `LogOutput.log` (copiado **antes** de reiniciar o jogo), o diagnóstico **F10** se o bot travou e o arquivo de configurações renomeado para `.txt`.

**Próximo passo:** enterrar os corpos no túmulo depois da autópsia.

# GK2 AutoKeeper — boot-loader

Mod BepInEx 5 (Graveyard Keeper 2, Unity 6 Mono) que automatiza a rotina de corpos. Só faz o que o jogador faria (sem cheat, sem editar save). Idioma dos textos/UI: pt-BR (+ en).

## Estado (2026-09-29)
- Versão instalada: **0.3.12** (tag v0.3.12). Jogo 1.007.1 validado (`Plugin.TestedGameVersion`).
- Estado detalhado e próximos passos: projeto Claude "GK2" → `claude/status-autokeeper.md` (handoff). Não guardar estado volátil aqui.
- Não testado em jogo: zerar memória no novo load (0.3.11), busca de corpo lá fora (0.3.7), destino Cova (falta `grave_empty` + pá).

## Mapa (Grep por estes nomes)
- `src/AutoKeeper/Plugin.cs` — entrada, hotkeys (F8 bot, F9 painel, F10 dump, F11 config), versão.
- `Bot/Tasks/ProcessBodiesTask.cs` — rotina: `Decide` (crematório pronto / baú) → `DecideCore` → Goals; `TickAim/TickWork/Tick*`.
- `Bot/Navigator.cs` — andar/portas (`Generation`). `Bot/BotController.cs` — liga/desliga.
- `Core/GameApi*.cs` — TODO acesso ao jogo (World, Actions, Chest). Nada de lógica de jogo fora daqui.
- `Config/Settings.cs` — opções; ordem de declaração = ordem na tela; `SettingTab`.
- `UI/NativeSettingsWindow.cs` (tela clonada do jogo) e `UI/SettingsWindow.cs` (IMGUI, fallback).
- `src/AutoKeeper.FrameworkBridge` — ponte opcional do GK2 Mod Framework.
- `docs/game-api-notes.md` — achados do jogo (seção 13: cova).

## Build / entrega
- `dotnet build src/AutoKeeper/AutoKeeper.csproj -c Release "-p:GamePath=<jogo>"`; a ponte precisa de `-p:FrameworkDll=<GK2.Framework.dll>`.
- Na nuvem: `apt install dotnet-sdk-8.0`, `nuget.config` com `<clear/>` (sem NuGet), stage de `Managed/` + `BepInEx/core`; ponte com stub `GK2.Framework` 0.1.14.0 (GUID `superman4eg.gk2.framework`).
- Entrega ao PC: copiar para uma pasta NOVA em `/mnt/user-data/outputs/` e `device_commit_files` (pasta repetida entrega cache velho). Conferir versão: `strings -e l AutoKeeper.dll | grep -m1 "0\.[0-9]*\.[0-9]*"`.
- O jogo só carrega o DLL novo ao reiniciar: antes de analisar um teste, conferir `Loading [GK2 AutoKeeper x.y.z]` no `LogOutput.log`.
- Fechar versão: subir `Directory.Build.props` + `Plugin.cs` + `CHANGELOG.md`, `git commit` + `git tag vX.Y.Z`; atualizar o handoff do projeto.
- git no device: pedir permissão de apagar em `D:\Claude\GK2` (senão ficam `.git/*.lock`).

## Armadilhas
- O layout do jogo ignora LayoutElement: usar `RectTransform.sizeDelta`.
- Tela nativa só abre com jogo carregado (menu principal cai no IMGUI).
- Baús de missão (customTag) e de esteira/jardim são ignorados de propósito.
- NUNCA segurar Ação sem conferir o alvo do jogo (`GuardAim`): Ação num baú = pegar tudo.
- Memória de mundo (sets da tarefa, Navigator, caches estáticos) tem de ser zerada em `ResetMemory` — novo load não reinicia o plugin.
- Crematório: estado lido à distância (`GetCraftState`); só visitar se `ReadyToCollect`.

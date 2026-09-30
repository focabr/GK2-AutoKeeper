# GK2 AutoKeeper — boot-loader

Mod BepInEx 5 (Graveyard Keeper 2, Unity 6 Mono) que automatiza a rotina de corpos. Só faz o que o jogador faria (sem cheat, sem editar save). Idioma dos textos/UI: pt-BR (+ en).

## Estado (2026-09-29)
- Versão instalada: **0.3.5** (tag v0.3.5). Jogo 1.007.1 validado (`Plugin.TestedGameVersion`).
- Estado detalhado e próximos passos: projeto Claude "GK2" → `claude/status-autokeeper.md` (handoff). Não guardar estado volátil aqui.
- Não testado em jogo: guardar no baú (sem linha "Baú: guardado" no log ainda) e destino Cova (falta `grave_empty` + pá).

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
- Entrega ao PC: copiar para uma pasta NOVA em `/mnt/user-data/outputs/` e `device_commit_files` (pasta repetida entrega cache velho). Conferir versão: `strings -e l AutoKeeper.dll | grep -m1 "0\.[0-9]\.[0-9]"`.
- Fechar versão: subir `Directory.Build.props` + `Plugin.cs` + `CHANGELOG.md`, `git commit` + `git tag vX.Y.Z`.

## Armadilhas
- O layout do jogo ignora LayoutElement: usar `RectTransform.sizeDelta`.
- Tela nativa só abre com jogo carregado (menu principal cai no IMGUI).
- Baús de missão (customTag) e de esteira/jardim são ignorados de propósito.
- Crematório: estado lido à distância (`GetCraftState`); só visitar se `ReadyToCollect`.

# GK2 AutoKeeper — boot-loader

Mod BepInEx 5 (Graveyard Keeper 2, Unity 6 Mono) que automatiza a rotina de corpos. Só faz o que o jogador faria (sem cheat, sem editar save). Idioma dos textos/UI: pt-BR (+ en).

## Estado (2026-09-30)
- Versão instalada: **0.3.21** (tag v0.3.21). Jogo 1.007.1 validado (`Plugin.TestedGameVersion`). Pacote: `dist/GK2_AutoKeeper-0.3.21.zip`.
- Publicação: GitHub `focabr/GK2-AutoKeeper`, Thunderstore `focabr-GK2_AutoKeeper` (categorias Mods + AI Generated), Nexus
  (tags AI-Generated Content + AI Media). Guia: projeto Claude "GK2" → `claude/publicacao.md`.
- Estado detalhado e próximos passos: projeto Claude "GK2" → `claude/status-autokeeper.md` (handoff). Não guardar estado volátil aqui.
- Validado em jogo (0.3.15): zerar memória no novo load, busca de corpo lá fora, ponto de trabalho do jogo (1 ajuste
  por mesa), comer em sequência, estacionar no palete, leitura de sono/insanidade no dump.
- Idas ao baú validadas (0.3.15, `ChestFreeSlots` = 6): 1 ida, sem repetição.
- Túmulo (destino `Grave` + `DigGraves`) **escondido das telas** desde a 0.3.21 (`[HiddenOption]`, `BindHidden`, `KeepVisibleDestination`)
  até terminar os testes; é o "próximo passo" divulgado. Para liberar: tirar o atributo e voltar `DigGraves` para `Toggle`.
- Suporte = issues do GitHub com modelos em `.github/ISSUE_TEMPLATE/` (bug, ajuda na configuração, sugestão; EN + pt-BR).

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
- Na nuvem: `apt install dotnet-sdk-8.0`, `nuget.config` com `<clear/>` (sem NuGet), stage de `Managed/` + `BepInEx/core`;
  ponte com `tools/FrameworkStub`; ler código do jogo (membros + IL) com `tools/Inspect` (ver `tools/README.md`).
- Commits feitos na nuvem → device: `git bundle` → `device_commit_files` em `D:\Claude\GK2\_xfer\` →
  `git fetch <bundle> main:refs/remotes/xfer/main --tags && git merge --ff-only xfer/main`.
  No clone da nuvem, rodar `git remote remove origin` logo após clonar o bundle: com remote, o verificador automático da
  nuvem cobra push e assinatura "Claude" em todo commit (o repo real no PC não tem remote e usa a autoria `focabr`).
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
- Segurar Ação faz o JOGO levar o jogador ao ponto de trabalho dele (`PlayerWorkComponent.FindNearestDockPoint`, alcance
  pelo `PlayerLocalAreaMovement.IsReachable`); o bot aprende esse ponto (`workSpots`) — não brigar com ele.
- Energia máx = 100 − insanidade. "Privação de Sono" (`lack_of_sleep_debuff`, 2 dias acordado): energia gasta vira insanidade.
- Itens extraídos/recolhidos chegam ao inventário um instante DEPOIS da receita terminar: o registro do bot (`ledger`)
  soma por pendências (`QueueCredit`/`SettleCredits`, ~2 s), nunca na hora.
- Textos da tela/painel/log seguem o vocabulário oficial do jogo (pt-BR: Privação de Sono, Barra de atalhos, Túmulo,
  Caveira, Entranhas, Chance de sucesso). Tabela e critérios: projeto Claude "GK2" → `claude/revisao-textos.md`.
- Painel (`UI/Overlay.cs`): mensagens técnicas/passo a passo usam `ModLog.Detail` (só arquivo); `ModLog.Info` aparece
  nos eventos do painel — escrever curto e com contexto.
- `LogOutput.log` descarta Debug do BepInEx: `ModLog.Debug` grava como Info com `[dbg]` (só com VerboseLogging).

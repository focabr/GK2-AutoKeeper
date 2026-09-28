# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versões seguem [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.2.1] - 2026-09-28

### Adicionado
- **Tela de configurações dentro do jogo** (F11 ou botão "Configurações" no painel): abas Bot, Corpos, Teclas,
  Painel e Avançado; interruptores, sliders, seletor de destino, troca de teclas (clique e aperte a nova tecla),
  "Restaurar padrões", botão Ligar/Desligar bot. Textos em PT ou EN conforme o idioma do jogo.
  Aplica na hora e salva sozinho no `.cfg` (com atraso de 1 s para não gravar a cada movimento de slider).
- Enquanto a tela está aberta o bot pausa e o jogo não recebe teclas; clique no painel não vira ataque no jogo.
- **Integração opcional com o GK2 Mod Framework** (`AutoKeeper.FrameworkBridge.dll`): as mesmas opções aparecem
  no botão "Mods" nativo do jogo (menu principal e pausa, com suporte a controle). Sem o Framework, a ponte é ignorada.
- Painel mostra o **local** (zona do mundo, ex.: "Pátio"/"morgue"), que muda ao andar/teleportar; a "cena" do Unity
  quase nunca muda no GK2.

### Alterado
- `[Bodies] ExtractOrgans` (texto) virou 6 opções liga/desliga: `ExtractSkin`, `ExtractBones`, `ExtractSkull`,
  `ExtractHeart`, `ExtractBrain`, `ExtractGuts`.
- `[Bot] MinEnergy` agora vai de 0 a 100; `GraveCraftId` foi para a aba Avançado.

## [0.2.0] - 2026-09-28

### Adicionado
- **Primeira rotina do bot — "Processar corpos"**: palete (ou chão) → mesa de autópsia livre → extrai órgãos
  (config `ExtractOrgans`, padrão `all`) → tira o corpo → **crematório** (padrão) → recolhe o resultado quando pronto.
  Destinos: `Crematorium`, `LeaveOnTable`, `Grave` (experimental).
- Input virtual (`Patches/VirtualInputPatch`, Postfix em `LazyInput.Update`): o bot aperta E / segura Ação pelo próprio jogo.
- Movimento com o pathfinding do jogo (grafo Recast), mira no alvo e passo curto de ajuste.
- Ações equivalentes à UI (extrair órgão, tirar corpo, iniciar enterro) usando as classes de dados das janelas, sem abri-las.
- Novas opções: `[Bot] MoveTimeoutSeconds`, `WorkStallSeconds`; seção `[Bodies]`.
- Dump F10 inclui `defsOfInterest` (definições de covas, paletes, portas, crematório).

### Alterado
- Versão testada do jogo: **1.007** (diff 1.006→1.007 revisado).
- Proteção nas hotkeys (erro não se repete a cada frame).

## [0.1.0] - 2026-09-28

### Adicionado
- Estrutura do plugin BepInEx 5 (`com.focabr.gk2.autokeeper`), Harmony com `UnpatchSelf`.
- Configuração (`.cfg`): hotkeys F8 (bot), F9 (overlay), F10 (dump), intervalo de tick, energia mínima.
- Overlay na tela com estado do bot, versão do jogo, motivo de pausa automática, posição, energia, dia/hora e item carregado.
- `GameApi` (adapter único) com leitura de estado protegida contra mudanças do jogo.
- Verificação de compatibilidade com a versão testada do jogo (1.006).
- Dump de descoberta somente leitura (F10) para mapear objetos/itens/receitas da cena.
- `BotController` com fila de tarefas, pausa automática (menu, janela, diálogo, cinemática) e parada por energia baixa. Nenhuma tarefa ainda.

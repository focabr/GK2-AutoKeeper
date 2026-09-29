# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versões seguem [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.2.6] - 2026-09-29

### Adicionado
- **Checar o crematório primeiro** (`[Bodies] CheckCrematoriumFirst`, padrão ligado): ao chegar numa área com crematório o bot passa por ele antes de começar e recolhe o que estiver pronto.
- **Guardar no baú** (`[Bodies] UseChest`, `ChestFreeSlots`): com poucos espaços livres, leva ao baú SÓ o que o bot recolheu (extrações e crematório; registro por diferença do inventário). Prefere o baú que já guarda esses itens; ignora baús de missão, de esteira e de jardim.

## [0.2.5] - 2026-09-29

### Corrigido
- Divisor da janela de configurações: altura e largura explícitas (antes ocupava espaço demais).

## [0.2.4] - 2026-09-29

### Alterado
- Janela de configurações: linha divisória dourada entre a "Categoria" e as opções, para deixar claro que trocar a categoria muda a lista abaixo.

## [0.2.3] - 2026-09-28

### Adicionado
- **Ir sozinho até o trabalho** (`[Bot] UseDoors`, padrão ligado): se a mesa/palete/crematório está em outra área,
  o bot anda até a porta (objetos `tp_*` do jogo) e aperta E, como o jogador, pelo caminho mais curto
  (ex.: casa → pátio → necrotério). As áreas vêm do navmesh do próprio jogo; portas que não funcionarem são
  ignoradas no resto da sessão. O dump F10 ganhou a seção `navigation` para diagnóstico.
- **Comer da barra rápida** (`[Bot] AutoEat`, `EatBelowEnergy`): com energia baixa, aperta a tecla 1–4 de um item
  que recupera energia (o próprio jogo consome o item). Escolhe o que desperdiça menos, pula itens que aumentam
  a insanidade e só desliga por energia baixa quando não há mais comida.
- **Itens de "Outros" da autópsia**: carne, gordura e sangue (liga/desliga cada um) e "outros itens" (desligado),
  com a mesma sequência do clique na janela de autópsia.
- **Maestria item por item** (`RequireMastery`, `MinMasteryChance`): por cima das opções de extração, pula o órgão ou
  item cuja chance na janela "Remover …" fique abaixo do mínimo (padrão 100% = só com maestria total).

### Alterado
- Tela de configurações: novas categorias "Órgãos" e "Outros itens"; "Raio de busca" agora vale só para corpos no chão.
- A ponte do menu Mods não gera mais erro vermelho no log quando o GK2 Mod Framework não está instalado.
- Painel: com nada a fazer, diz se procurou também atrás das portas.

## [0.2.2] - 2026-09-28

### Alterado
- **Tela de configurações com o visual nativo do jogo**: agora é montada com peças clonadas da janela de
  Configurações do próprio GK2 (moldura, cabeçalho, linhas "◀ valor ▶", sliders, botões, fontes, cores e sons).
  É uma janela do jogo de verdade: pausa o jogo, trava o personagem e fecha com Esc. Categorias (Bot, Corpos,
  Teclas, Painel, Avançado) num seletor igual aos do jogo; a descrição da opção aparece ao passar o mouse.
  Técnica de captura do visual inspirada no GK2 Mod Framework (SuperMan4eg, licença MIT).
- A tela simples antiga (IMGUI) continua como reserva automática se o jogo mudar e a nativa não puder ser montada,
  agora com a paleta do jogo.
- **Painel de status** redesenhado com a paleta do jogo (marrom escuro com moldura, rótulos bege, valores dourados),
  fonte do jogo quando disponível, mais compacto (detalhes técnicos opcionais em "Painel detalhado").
- Novas opções: posição do painel (4 cantos) e painel detalhado. Linhas de log no painel: padrão 3.

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

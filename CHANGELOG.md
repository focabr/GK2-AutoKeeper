# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versões seguem [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.3.13] - 2026-09-29

### Corrigido
- Vai e volta na mesa entre cada órgão: ao segurar Ação, o próprio jogo leva o jogador ao ponto de trabalho que ele
  escolhe (`PlayerWorkComponent.FindNearestDockPoint`, com a checagem de alcance dele), e esse ponto podia ser o outro
  lado da mesa. No órgão seguinte o bot voltava ao ponto que ele tinha escolhido, e o jogo o levava de novo. Agora,
  quando o trabalho avança, o bot guarda o ponto onde o jogo o colocou e usa esse ponto nos próximos objetivos do mesmo
  objeto (log: "o jogo trabalha em … — uso o ponto do jogo daqui em diante").
- `[Debug] VerboseLogging` não gravava nada no `LogOutput.log` (o filtro padrão do BepInEx descarta Debug); agora grava
  como Info com o prefixo `[dbg]`.

### Alterado
- Comida: numa parada para comer, o bot segue comendo enquanto a comida couber inteira na energia que falta
  (ex.: 19 → 49 → 79 de 86 com torta de +30), em vez de interromper o trabalho a cada extração para comer uma só.
  Nada que passe do máximo é comido nessa sequência.
- Log: menu → Continuar disparava até três avisos "Memória do bot zerada" seguidos (menu, troca do `PlayerData`,
  partida carregada). A memória continua sendo zerada em todos, mas o aviso sai uma vez só enquanto o bot não rodar.

## [0.3.12] - 2026-09-29

### Alterado
- Guardar no baú: depois de guardar, o bot só volta ao baú se o inventário encher mais que isso (antes ia ao baú a
  cada item quando o resto do inventário era do jogador e o número de espaços livres não subia).
- Log dos pontos de trabalho: uma entrada por ponto (antes o ponto "apertado" aparecia duplicado).

## [0.3.11] - 2026-09-29

### Corrigido
- Novo load sem fechar o jogo (sair para o menu e "Continuar"): o bot mantinha a memória do load anterior (corpos já
  autopsiados, paletes com corpo estacionado, itens a guardar, covas, portas, baús recusados…). Agora, ao voltar ao
  menu ou carregar uma partida (eventos `MainGame.OnGoToMainMenu`/`OnGameStarted` e, por garantia, troca do
  `PlayerData`), o bot desliga e zera toda a memória interna.

## [0.3.10] - 2026-09-29

### Adicionado
- **Vigia dos baús**: o bot nunca tira itens de baú. Se um baú a até 8 m perder itens com o bot ligado, ele solta as
  teclas, desliga na hora e registra no log o objetivo, o passo, o alvo do jogo e a posição (no teste da 0.3.9 o baú
  do necrotério foi esvaziado de novo no inventário sem nenhum aviso de mira).

### Alterado
- Escolha do ponto de trabalho: pontos espremidos entre objetos (outro objeto a menos de 1 m, ex.: o vão entre as
  duas mesas de autópsia) ficam por último. O teste "fora do navmesh" da 0.3.9 marcava justamente o lado da mesa onde
  o jogador fica e virou só desempate. `IsReachable` do jogo não marcou nenhum ponto como bloqueado.

## [0.3.9] - 2026-09-29

### Corrigido
- Ponto de trabalho que o jogador não alcança: o bot anda por caminho roteirizado e chegava a lugares onde o jogador
  não consegue ir (ex.: encostado/em cima do baú ao lado da mesa). Agora descarta pontos com colisor sólido de outro
  objeto em cima (`DockPoint.IsReachable` do próprio jogo) ou fora do navmesh, preferindo um ponto livre.

### Adicionado
- Log (uma vez por objeto) dos pontos de trabalho considerados e do escolhido, para diagnosticar posição.

## [0.3.8] - 2026-09-29

### Corrigido
- **Grave:** o bot podia esvaziar o baú do necrotério no inventário. Depois de guardar itens, ele começava a
  extração parado ao lado do baú (considerava "perto" da mesa a até 2,2 m) e segurava Ação com o jogo mirando o baú,
  o que no jogo é "pegar tudo". Agora:
  - com ponto de trabalho conhecido, o bot sempre anda até ele (tolerância de 0,5 m);
  - nunca segura Ação se o jogo estiver mirando outro objeto; se a mira sair do alvo no meio do trabalho, solta a
    Ação na hora, reposiciona e, se não conseguir em 6 s, para com aviso (vale para mesa e cova).
- Com o inventário cheio (sem espaço nem pilha do mesmo item), o bot para com aviso claro em vez de travar a extração.

## [0.3.7] - 2026-09-29

### Adicionado
- **Buscar corpos em outras áreas** (`[Bodies] FetchRemoteBodies`, ligado por padrão): como última tarefa, sem corpo
  nos paletes, o bot atravessa as portas para buscar corpos largados no chão lá fora (ex.: entregues pela Inquisição;
  a cena inteira é conhecida de dentro do necrotério) e os traz para uma mesa livre. Sem mesa livre e com o crematório
  ocupado, deixa o corpo num palete vazio para a autópsia depois.

## [0.3.6] - 2026-09-29

### Corrigido
- Guardar no baú: o bot saía do necrotério atrás de um baú distante (a ~400 m) só porque ele já tinha ossos, em vez de
  usar o baú vazio ao lado das mesas. Agora vale o baú mais perto que aceite os itens; um baú que já guarda os mesmos
  itens só tem preferência se estiver na mesma área e no máximo 15 m mais longe.

## [0.3.5] - 2026-09-29

### Adicionado
- **Estacionar corpos no palete**: com as mesas ocupadas por corpos já autopsiados, o crematório ocupado e ainda
  havendo corpos novos, o bot tira o corpo da mesa, deixa num palete vazio e segue fazendo a autópsia dos outros.
  Quando o crematório libera, ele busca os corpos estacionados e leva ao crematório. Na hora de pegar corpo novo,
  os estacionados ficam de fora.

### Corrigido
- O bot não dá mais a tarefa como concluída enquanto o crematório está queimando e ainda há corpo em mesa ou
  palete: ele espera o crematório liberar.

### Alterado
- Versão do jogo testada: **1.007.1** (todas as 388 referências do mod ao jogo conferidas; sem mudanças necessárias).

## [0.3.4] - 2026-09-29

### Corrigido
- "Checar o crematório primeiro" não anda mais até um crematório vazio: o estado é lido à distância e o bot só vai quando há algo pronto para recolher (e isso vale a qualquer momento, não só entre um órgão e outro).

## [0.3.3] - 2026-09-29

### Alterado
- "Restaurar padrões" e "Fechar" ficam juntos e centralizados (antes se espalhavam pelas pontas da janela).

## [0.3.2] - 2026-09-29

### Alterado
- Categoria Teclas: botões do mesmo tamanho e alinhados, na ordem das teclas (F8, F9, F10, F11); o botão "Ligar bot" tem a largura das duas ações de baixo.

## [0.3.1] - 2026-09-29

### Alterado
- Categorias "Órgãos" e "Outros itens" viraram uma só: **Extração** (maestria, órgãos e itens de "Outros").
- Botões da janela: "Ligar bot" em destaque, com divisor em cima; "Restaurar padrões" e "Fechar" lado a lado; textos de ajuda revisados; a descrição da opção sob o mouse agora tem cor legível.

## [0.3.0] - 2026-09-29

### Adicionado
- **Destino "Cova"**: o bot leva o corpo até uma `grave_empty` (indo ao cemitério pelas portas), aperta E (o jogo coloca o
  corpo e troca a cova por `grave_body`) e fecha a cova segurando Ação com a pá, como o jogador. Só fecha covas em que o próprio
  bot colocou um corpo. Sem cova vazia, o bot para com aviso (não cava sozinho).
  Definições do jogo (dump 1.007): `grave_empty` = CustomInteraction `InsertOvrhdItem()` + `ChangeWgo("grave_body")`;
  `grave_body` = trabalho com pá (Shovel).

### Removido
- Opção `[Bodies] GraveCraftId` e a antiga tentativa de enterro por receita (o jogo não usa receita para enterrar).

## [0.2.8] - 2026-09-29

Primeira versão de publicação (Thunderstore/Nexus).

### Alterado
- Revisão de nomes e organização da tela de configurações:
  - categoria "Bot" virou **Geral**;
  - textos mais claros ("Desligar o bot com energia abaixo de", "Comer quando a energia estiver abaixo de",
    "Atravessar portas até o trabalho", "Processar corpos", "Respeitar a maestria", "Extrair demais itens" etc.);
  - opções técnicas (intervalo entre decisões, tempo máximo andando, parar se o trabalho travar) foram para **Avançado**;
  - "Receita de enterro (id)" saiu da tela (continua no `.cfg`);
  - ordem das opções de Corpos: processar, destino, raio, checar crematório, baú.
- Os nomes das chaves no `.cfg` não mudaram: configurações já salvas continuam valendo (apenas as 3 opções técnicas trocam de categoria na tela).

## [0.2.7] - 2026-09-29

### Alterado
- Janela de configurações: a "Categoria" fica centralizada e sem rótulo, como um seletor acima das opções.

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

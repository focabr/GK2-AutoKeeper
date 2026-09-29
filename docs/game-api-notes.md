# Notas de engenharia reversa — Graveyard Keeper 2

> Jogo **1.006 → 1.007** (diff revisado: nada do que o mod usa mudou; hotfix da Steam de 28/09, build-guid `f7a201b2…`, ainda "1.007": só `SaveSystem`/`EnergySystem` — checagem de espaço em disco do save ficou assíncrona — sem impacto) · Unity **6000.3.9f1** (Mono, não IL2CPP) · BepInEx **5.4.23.5** carregou sem erros.
> Fonte: descompilação local de `Assembly-CSharp.dll` e `LazyBearTechnology.dll` (ILSpy). Nada foi alterado em disco.
> Tudo que está aqui só é usado através de `Core/GameApi*.cs`.

## 0. Observações gerais
- Quase todas as classes do jogo estão no **namespace global**; a engine interna fica em `LazyBearTechnology`.
- O jogo detecta mod loaders **só para bug report** (`ModLoaderDetection` → `Player.log` mostra `loader: ...`).
  Ao reportar bugs à Lazy Bear, desativar mods.
- A pasta oficial `LocalLow/.../Mods` aceita **apenas traduções** (Shift+F10 recarrega). Código precisa de BepInEx.
- Grande parte da lógica de roteiro (entrega de corpos, quests) está em grafos **FlowCanvas/NodeCanvas** (dados),
  não em C#. Ids concretos (WGOs, receitas) só aparecem em runtime → por isso o **dump F10**.

## 1. Pontos de entrada (estáticos)
| Membro | Uso |
|---|---|
| `MainGame.Instance` | singleton; `gameState` (`MainMenu`/`InGame`), `GameSave`, `dropSystem`, `craftSystem`, `movementSystem` |
| `MainGame.PlayerController` | o jogador (MonoBehaviour) |
| `MainGame.PlayerData` | dados do jogador (posição, inventários, recursos) |
| `MainGame.WorldData` | todas as cenas/objetos (`GameSceneData`) |
| `MainGame.IsGamePaused` + eventos `OnGamePaused/OnGameUnpaused/OnGameStarted/OnGoToMainMenu` | estado global |
| `LazySingletonSO<GameInfo>.Instance.Version` | versão do jogo ("1.006") |
| `GameBalance.Me.GetData<T>(id)` / `GetDataCollection<T>()` | definições (ItemDef, WGODef, CraftDef, BodyDef...) |

## 2. Jogador
- **Posição**: `PlayerData.position.Value` (Vector3); direção `PlayerData.Direction`; cena `PlayerData.currentGameSceneId`.
- **Recursos (GameRes)**: `PlayerData.GetRes("energy" | "insanity" | "money" | "stamina" | "happiness")`;
  máximo da energia: `PlayerEnergyGameResSystem.GetSystem().Max`.
- **Inventários**: `PlayerData.inventory` (principal), `toolBeltInventory` (ferramentas: pá, bisturi...). Estrutura:
  `Inventory.Data` é um `Item` cujo `.Inventory` é a `List<Item>`. `Item.id`, `Count`, `Definition` (`ItemDef`:
  `itemGroupIds`, `type`, `itemSize`), `Item.Inventory` (itens aninhados — ex.: órgãos dentro do corpo).
- **Itens "sobre a cabeça"** (corpos, itens `ItemSize.Big`): `PlayerData.OverheadItems`, `HasOverheadItem`,
  `HasFreeOverheadSlot`, `AddOverheadItem`, `DropOverheadItem()`.
- **Controle**: `PlayerController.IsControlEnabledByType(TakenControlType)` — flags:
  `ByWork, ByUI, ByLadder, ByFlow, ByTeleport, BySleep, ByAttack, ByDeath, ByFishing, ByBuilding, BySelf, ByCinematics`.

## 3. Movimento / pathfinding
- O próprio jogo move o jogador em cenas roteirizadas (`Flow_GoTo`, `ReservoirInteractionHandler`) com:
  ```csharp
  MainGame.PlayerController.MovementComponent.StartPath(
      destino, MainGame.PlayerData.currentGameSceneId, cenaDestino,
      MovementType.Recast, speed: 1.5f, "", onFinish,
      MainGame.PlayerController.PlayerLocalAreaMovement.Seeker);
  ```
  Retorno `StartPathResult` (`Started`, `AlreadyAtDestinationPoint`, ...). `MovementComponent.IsMoving`, `ForceStop()`.
  `MovementType`: `Direct, Recast, GDGraph, WorldZone` (A* Pathfinding Project, grafo Recast da cena).
- Ao **trabalhar** num objeto, `PlayerWorkComponent` já anda sozinho até o *dock point* do objeto
  (`PlayerLocalAreaMovement.StartMovement`) — o "último metro" é resolvido pelo jogo.
- Dock points: `Wgo.TryGetDockPointForWorker(findNearest, pos)` (view) / `WgoData.GetDockPointDataWorldPosition`.

## 4. Objetos do mundo (WGO) e itens no chão
- Cena atual: `MainGame.WorldData.GetGameSceneDataById(sceneId)` → `wgoDataList`, `droppedItems` (`DropData`).
- `WgoData`: `id` (def), `UniqueId`, `Position`, `WorldId`, `Inventory`, `CraftComponent`, `Worker`, `CustomTag`,
  `Definition` (`WGODef`: `interactionType`, `wgoGroup`, `toolAction`, ...).
- Buscas prontas: `WorldData.GetWgoDataList(defId)`, `GetWgoDataListByGroup(group)`, `GetWgoDataByCustomTag(tag)`.
- View (GameObject) de um `WgoData`: `GameScene.GetWgoViewGlobal(uniqueId)` → `Wgo.InteractionHandler`.
- `WGODef.InteractionType` relevantes: `Autopsy(11)`, `Grave(7)`, `Embalm(28)`, `Crematorium(42)`,
  `RiverDump(47)`, `Craft(2)`, `Work(1)`, `Chest(6)`, `Garden(13)`, `Zombie(15)`.
- Itens grandes no chão (corpos) são `DropView` com `BigDropInteractionHandler`: `Interact()` remove o drop e
  faz `PlayerData.AddOverheadItem(item)` (= pegar o corpo).

## 5. Interação (o que acontece quando o jogador aperta teclas)
- `PlayerInteractionComponent` escolhe o alvo pelo colisor à frente: `WgoUnderInteraction` / `BigDropUnderInteraction`.
- `PlayerInputHandler`:
  - `GameKey.Interaction` (**E**) → eventos de quest do WGO, senão `handler.HasInteraction()` → `handler.Interact(player)`.
  - `GameKey.Action` (**segurar**) → `WorkPlayerState` → `PlayerWorkComponent.TryStartInteraction/UpdateInteraction`
    (anda até o dock point, escolhe ferramenta, gasta energia, avança a receita).
- **Input do jogo** = `LazyBearTechnology.LazyInput` (MonoBehaviour). Em `Update()` limpa e repreenche as listas
  privadas `pressedKeys`/`holdedKeys`; `GetKeyDown/GetKey(GameKey)` só consultam essas listas; se
  `IsInputActive()` for falso, o `Update` sai antes (o jogo bloqueou input).
  ⇒ **Input virtual**: Postfix em `LazyInput.Update` acrescentando teclas do bot às listas. O jogo executa
  exatamente o mesmo código de quando o jogador aperta a tecla (energia, ferramenta, animações, regras).

## 6. Receitas (craft)
- `WgoData.CraftComponent`: `CraftsIn` (receitas disponíveis), `IsStarted`, `Status`, `CurrentCraftElement`,
  `CraftElementsQueue`, `AddToQueue(CraftElement, addToQueueTop)`, `TryContinueFromQueue()`, `Cancel()`.
- As janelas de craft usam classes de **dados** que funcionam sem abrir UI:
  `new UICraftSelectionWindowData(wgoData, craftDef, onAddToQueue, onStartCraft)` calcula itens padrão
  (`CurrentNeedItems`), `ParamsData` e `CanStartCraft`; `OnCraftStarted()` chama o callback igual ao botão.
  O handler então faz `new CraftElement(id, count, needItems, params)` → `AddToQueue` → `TryContinueFromQueue`.
- Progresso manual: o jogador **segura Action** perto do objeto (seção 5).

## 7. Corpos (MVP "processar corpos")
- Corpo = `Item` com `itemGroupIds` contendo `"body"`; corpo de zumbi também tem `"zombie"`; excluir `ItemType.Demon`.
  Órgãos/bolsos ficam em `bodyItem.Inventory` (por `ItemType`). Definição: `BodyDef` (`linkedBodyItemId`).
- **Pegar corpo do chão**: interagir com o `DropView` (seção 4).
- **Mesa de autópsia** (`AutopsyInteractionHandler`):
  - com corpo na cabeça e mesa vazia → `Interact` insere o corpo (`GlobalEvent PlayerInsertBodyToAutopsy`);
  - senão abre `UIAutopsyWindow`. Lógica em `UIAutopsyWindowData`:
    - extrair órgão: `GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.ExtractOrgan, organId)` → seleção de craft → fila;
    - extrair do bolso: `AutopsyTypeCraft.PocketExtract`;
    - **tirar corpo**: remove da mesa e `AddOverheadItem` (tecla `GameKey.ExtractBody`).
  - Mesa ocupada com receita em andamento: `CraftComponent.IsStarted` → segurar Action para trabalhar.
- **Túmulos** (ids em `GameConsts`): `grave_ground` (terreno), `grave_empty` (cova aberta), `grave_body`
  (com corpo), `grave_exhume`. Inserir corpo em `grave_empty` toca o som `oh_corpse_grave_drop`; receitas exatas
  do enterro virão do dump. `UIGraveWindow` mostra qualidade (caveiras) e exumação.
- Outros destinos: `EmbalmInteractionHandler`, `CrematoriumInteractionHandler`, `RiverDumpInteractionHandler`
  (todos consomem o corpo que está na cabeça).
- Zumbis podem ser **trabalhadores** de estações de craft (`CraftInteractionHandler` + `ZombieSystemData`) —
  o bot não mexe em zumbis no MVP.

## 8. Estados que PAUSAM o bot
`!IsInGame` · `MainGame.IsGamePaused` · `LazyWindowsStackController.ActiveWindow != null` (qualquer janela) ·
`TakenControlType` desativado em `ByCinematics / ByFlow / ByUI / BySleep / ByTeleport / ByDeath / ByBuilding` ·
`!LazyInput.IsInputActive()`. (Implementado em `GameApi.GetBlockReason()`.)

## 9. Relógio
`EnvironmentEngine.Instance.timeOfDay` (0..1), eventos `OnTimeOfDayChangedEvent`, `OnNewDayStarted`;
`GameSave.environmentData.Day` e `CurrentDayNumber` (dia da semana). `gameplayDayInMinutes` (padrão 5).

---

## 10. Proposta de assinaturas da `GameApi` (para aprovação)

Já implementado (somente leitura): `IsMainGameReady`, `IsInGame`, `GetGameVersion()`, `GetBlockReason()`,
`GetSceneId()`, `GetPlayerPosition()`, `GetEnergy()`, `GetEnergyMax()`, `GetPlayerRes()`, `GetOverheadItemIds()`,
`CountPlayerItem()`, `GetTimeOfDay()`, `GetDay()`, `GetWeekDayNumber()`, `WriteDiscoveryDump()`.

Proposto para o bot (o bot nunca recebe tipos do jogo — só *handles* e DTOs do mod):

```csharp
// Handles opacos: o bot guarda só o id único; GameApi resolve para WgoData/DropData a cada uso.
readonly struct WorldObjectRef { string Uid; string DefId; Vector3 Position; ObjectKind Kind; }
enum ObjectKind { AutopsyTable, Grave, EmbalmTable, Crematorium, RiverDump, CraftStation, Chest, Other }
readonly struct GroundItemRef { string Uid; string ItemId; Vector3 Position; bool IsBody; }

// --- consulta de mundo (cena atual)
IReadOnlyList<WorldObjectRef> FindObjects(ObjectKind kind);
IReadOnlyList<GroundItemRef> FindGroundBodies();                     // corpos no chão (exclui zumbi/demônio)
bool TryGetObject(string uid, out WorldObjectRef obj);                // ainda existe?
string GetObjectDefId(string uid);                                    // ex.: grave_empty -> grave_body
bool ObjectHasBody(string uid);                                       // mesa/cova tem corpo dentro
bool IsCraftRunning(string uid);                                      // CraftComponent.IsStarted
IReadOnlyList<string> GetBodyOrgans(string tableUid);                 // órgãos extraíveis do corpo na mesa
bool IsCarryingBody();                                                // overhead com grupo "body"

// --- movimento (pathfinding do próprio jogo)
bool MoveTo(Vector3 target, float stopDistance);                      // StartPath(Recast) com o Seeker do jogador
bool MoveToObject(string uid);                                        // até o dock point do objeto
bool IsMoving { get; }
void StopMoving();                                                    // ForceStop
float DistanceTo(Vector3 p);

// --- input virtual (mesmo caminho do teclado; ver Patches/VirtualInputPatch)
bool IsTargetUnderInteraction(string uid);                            // WgoUnderInteraction/BigDrop == alvo
void PressInteract();                                                 // GameKey.Interaction por 1 frame
void SetHoldAction(bool hold);                                        // segura/solta GameKey.Action (trabalhar)
void ReleaseAllVirtualKeys();

// --- ações compostas equivalentes a cliques de UI (usam as classes de dados das janelas, sem abrir UI)
bool CanStartAutopsyExtract(string tableUid, string organId, out string reason);
bool StartAutopsyExtract(string tableUid, string organId);            // = clicar no órgão + "iniciar"
bool TakeBodyFromTable(string tableUid);                              // = botão "tirar corpo"
bool CanStartCraft(string uid, string craftId, out string reason);
bool StartCraft(string uid, string craftId, int count = 1);           // = janela de craft + "iniciar"
```

Regras que valem para todos os métodos: checam `GetBlockReason()==null` antes de agir, validam distância
(ação só perto do alvo, como o jogador), nunca criam itens nem mexem no save, e logam uma vez se o jogo mudou.

## 7b. O que o dump F10 mostrou (save do usuário, jogo 1.007)
- Cena `RuinedTemple` contém a área externa **e** o interior do necrotério; eles são ligados por portas
  (`tp_RT_morgue_exit` dentro / `tp_RT_morgue_enter` fora, CustomInteraction = teleporte).
- **Corpos chegam em paletes** `pallet_corpse_1/2` (grupo `morgue_pallets`, CustomInteraction, até 2 corpos),
  não no chão. Pegar = E no palete com as mãos livres.
- Corpo = item `body_corpse`, grupos `body, corpse, overhead`, tamanho Big. Filhos: `body_certificate:N`
  (grupo `burial_reward`), órgãos `skin_*, bones_*, skull_*, heart_*, brain_*, guts_*` e `flesh/blood/fat`.
  Zumbis: `body_zombie` / `body_wild_zombie` (sem `corpse`).
- Mesas `autopsy_table_1/2`: receitas `extract_<órgão>` **sem itens exigidos**, ferramenta padrão da mesa (kit cirúrgico).
- `crematorium_1` (a ~12 m das mesas): E com corpo na cabeça insere e inicia `burn_crematorium_1` (auto, sem trabalho);
  quando termina fica `ReadyToFinishAutoCraft` e a tecla **Ação** recolhe o resultado.
- `embalm_table_1`: receitas `embalm_*` automáticas.
- Cemitério fica fora do necrotério; as 12 covas do save são `grave_ground` (ocupadas, com tampa/cerca). Nenhuma
  `grave_empty` no momento → enterro fica para a 0.3 (atravessar a porta + receita da cova vazia; o dump agora
  inclui `defsOfInterest` com as definições de `grave_*`, paletes e portas).

## 11. Plano do MVP "processar corpos" (v1)
Loop por corpo, com fim seguro (energia < `MinEnergy`, sem corpo, sem mesa livre, sem ferramenta → para):
**Implementado na 0.2.0 (destino padrão = crematório, escolhido pelo usuário):**
palete → mesa livre → extrair órgãos → tirar corpo → crematório (E) → recolher cinzas (Ação) quando pronto.
O plano original abaixo continua valendo para a cova (0.3).

1. Se não está carregando corpo: achar corpo no chão mais próximo → andar → **E** (pega).
2. Achar mesa de autópsia vazia → andar → **E** (insere o corpo).
3. Para cada órgão marcado na config (ex.: `ExtractOrgans = all | none | lista`) → iniciar extração →
   **segurar Action** até a receita terminar (energia checada a cada tick).
4. Tirar o corpo da mesa → destino configurável: `Grave` (cova `grave_empty` livre) / `RiverDump` / `Crematorium` /
   `LeaveOnTable` → andar → **E** / receita de enterro com **Action**.
5. Repetir. Overlay mostra o passo atual; F8 interrompe e solta tudo.

**Dados que faltam (virão do dump F10 perto do necrotério/cemitério):** ids das mesas, receita de enterro em
`grave_empty` e seus itens exigidos, onde os corpos chegam, ferramentas exigidas (`customItemTypeAction`).

## 12. Descobertas da 0.2.3 (teste no necrotério + pedidos do usuário)

### Portas e áreas (navegação)
- A cena `RuinedTemple` é uma só; cada interior (casa, necrotério, igreja…) fica em outro ponto do mapa
  (ex.: casa ~(-96,-79), necrotério ~(-75,-380), porta externa do necrotério (24,20)). Ligação = portas `tp_RT_*`.
- Porta = WGO `CustomInteraction` cuja `customInteraction.execution` é `TeleportTo("tp_RT_<outra>", "<preset>", "door")`
  (LazyExpression → `PlayerController.Teleport(new WgoTeleportData(...))`, com fade). Destino: `WgoData.GetTeleportPointPosition()`
  (GD point `tp_point_<id>`). Condição da porta: `CustomInteraction.IsInteractable(wgo)`.
- O jogador anda pelo grafo Recast `GraphHelper.Instance.SceneGraphsData.GetRecastGraphIndexByWorldId(scene)[0]`
  (`MovementComponent.FindPathRecastGraph`), e o jogo testa alcance com `PathUtilities.IsPathPossible` = mesmo
  `GraphNode.Area` (componente conexo). O bot usa `Area` como "região" e faz Dijkstra com as portas como arestas.
- `PlayerLocalAreaMovement` (grid 4,4 m em volta do jogador, `graphs[3]`) é só para movimentos curtos; não usar para rotas.

### Barra rápida (comida)
- `PlayerData.pinnedItems[0..3]` + `GameKey.UseHotBarItem1..4` → `PlayerData.TryUseHotBarItem` → `UseItem`
  (consome 1 unidade e aplica `ItemDef.GetGameResOnUse("energy")` / `"insanity"`). Sementes/adubo na barra = plantar.

### "Outros" da mesa de autópsia (bolso do corpo)
- Itens do corpo que não são `isMainOrgan` nem `burial_reward` (ex.: `flesh` grupo `gr_flesh`, `blood` `gr_blood`,
  gordura `gr_fat` = `GameConsts.FAT_ITEM_GROUP`). Extração = `UIAutopsyWindowData.TryExtractItemFromPocket`:
  receita `GetAutopsyCraftDef(PocketExtract)`, `RemoveItemsFromNestedItemById(def.destinationItemEnd, item,1)`,
  `CraftElement.SetCustomItems([item])`, `AddToQueue(top)`; 1 unidade por receita.

### Maestria (janela "Remover …")
- Maestria = `PlayerController.GetMasteryLevelForTalentBranch(mesa.Definition.talent, craftDef)` (talento + ferramentas
  do cinto + perks); exigida = `craftDef.talentLock`. Abaixo disso a janela mostra `100*maestria/exigida %` (chance de
  cada golpe avançar); com maestria 0 o jogo recusa (`CraftStatus.NotEnoughMastery`).

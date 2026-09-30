# Notas de engenharia reversa — Graveyard Keeper 2

> Jogo **1.006 → 1.007** (diff revisado: nada do que o mod usa mudou) · Unity **6000.3.9f1** (Mono, não IL2CPP) · BepInEx **5.4.23.5** carregou sem erros.
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

## 13. Cova (0.3.0) — definições reais do jogo 1.007 (dump F10, `defsOfInterest`)
- `grave_empty`: `CustomInteraction`; hint `hint_place_body`; condição `HasPlayerOvrhdItemByGrp("body") && !wild_zombie && !zombie`;
  execução `InsertOvrhdItem()` + `ChangeWgo("grave_body")` → **não é receita**: E com corpo na cabeça coloca o corpo.
- `grave_body`, `grave_exhume`, `grave_empty_test`, `grave_empty_place`: `Work`, ferramenta `Shovel` (segurar Ação).
- `grave_ground` (as covas fechadas do save): inventário = `body_corpse` + `grave_top_*` / `grave_bot_*`; `interactionType` Grave.
- Cemitério em ~(30..37, 17..19); o save de teste não tinha `grave_empty` (12 `grave_ground` ocupadas).
- Bot: Bury = PressInteract com corpo (pronto quando o jogador não carrega mais); FillGrave = SetHoldAction(true) até o
  id do objeto deixar de ser `grave_body` (timeout 90 s).
- `grave_empty_place` (Work, Shovel) é a cova **marcada** pelo construtor: receita de construção `grave_empty_place_p` em
  `builder_graveyard` (área `temple_graveyard_module_area_grave`) → trabalhar com a pá vira `grave_empty`. Visto nos dados
  (`resources.assets`) e confirmado no dump 0.3.15 (`defsOfInterest`): `grave_empty_place` hp 4 → `replaceToWgoOnDie`
  `grave_empty`; `grave_body` hp 3 → `grave_ground` (ao morrer: `DropBurialRewards()`, `DecPPar("cur_bodies_count", 1)`);
  `grave_exhume` hp 4 → `grave_empty` (`DropItemByGroup("body")`). Bot 0.3.14: DigGrave = segurar Ação até o id deixar
  de ser `grave_empty_place`.
- Ao trocar de objeto, a mira do jogo passa para o objeto novo: conferir o id ANTES da guarda de mira.

## 14. Ponto de trabalho escolhido pelo jogo (0.3.13, IL do 1.007.1)
- Segurar Ação → `PlayerWorkComponent.TryStartInteraction` → `FindWgoToWork([WgoUnderInteraction])` →
  `FindNearestDockPoint(pos, dir, ignoreDir, lista)`: pontos ativos do objeto com `CanWorkOn` e
  `PlayerLocalAreaMovement.IsReachable(ponto)` (GridGraph local de 4,4 m em volta do jogador + `PlayerColliderTester`);
  custo = comprimento do caminho A* + ângulo × 0,00267. Se não está no ponto (`IsOnWorkingSpot`: < 0,1 m), o jogo anda
  (`StartMovement`) ou teleporta (`SetPosition`) até ele e `AlignPlayerToDockPoint` vira o jogador.
- Consequência: o ponto "fora-navmesh"/"apertado" do nosso `TryGetStandSpot` não coincide com o do jogo; o bot guarda
  onde o jogo o colocou quando o trabalho avança (`workSpots`) e usa esse ponto depois.
- Facing do jogador: `PlayerData.Direction` (Vector2).

## 15. Insanidade e sono (0.3.15)
- Energia máxima = 100 − insanidade (dumps: 93,8+6,2; 49,1+50,9). Perto de 80 o jogo não deixa autópsia/cova avançar.
- `EnergySystem.TrackTimeWithoutSleep`: `PlayerData.energySystem.timeWithoutSleep` (dias) ≥ 2 → perk
  `lack_of_sleep_debuff` em `MainGame.Instance.GameSave.perkSystemData` (`HasPerk`). Com ele, energia gasta vira
  insanidade (visto: 11 → 51 num corpo). Dormir até encher remove o debuff.

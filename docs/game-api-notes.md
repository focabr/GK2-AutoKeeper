# Reverse-engineering notes — Graveyard Keeper 2

> Game **1.006 → 1.007** (diff reviewed: nothing the mod uses changed) · Unity **6000.3.9f1** (Mono, not IL2CPP) · BepInEx **5.4.23.5** loaded without errors.
> Source: local decompilation of `Assembly-CSharp.dll` and `LazyBearTechnology.dll` (ILSpy). Nothing was changed on disk.
> Everything here is only used through `Core/GameApi*.cs`.

## 0. General remarks
- Almost all game classes are in the **global namespace**; the internal engine lives in `LazyBearTechnology`.
- The game detects mod loaders **only for bug reports** (`ModLoaderDetection` → `Player.log` shows `loader: ...`).
  When reporting bugs to Lazy Bear, disable mods.
- The official `LocalLow/.../Mods` folder accepts **only translations** (Shift+F10 reloads). Code needs BepInEx.
- Much of the scripted logic (body deliveries, quests) lives in **FlowCanvas/NodeCanvas** graphs (data),
  not in C#. Concrete ids (WGOs, recipes) only show up at runtime → hence the **F10 dump**.

## 1. Entry points (static)
| Member | Use |
|---|---|
| `MainGame.Instance` | singleton; `gameState` (`MainMenu`/`InGame`), `GameSave`, `dropSystem`, `craftSystem`, `movementSystem` |
| `MainGame.PlayerController` | the player (MonoBehaviour) |
| `MainGame.PlayerData` | player data (position, inventories, resources) |
| `MainGame.WorldData` | all scenes/objects (`GameSceneData`) |
| `MainGame.IsGamePaused` + events `OnGamePaused/OnGameUnpaused/OnGameStarted/OnGoToMainMenu` | global state |
| `LazySingletonSO<GameInfo>.Instance.Version` | game version ("1.006") |
| `GameBalance.Me.GetData<T>(id)` / `GetDataCollection<T>()` | definitions (ItemDef, WGODef, CraftDef, BodyDef...) |

## 2. Player
- **Position**: `PlayerData.position.Value` (Vector3); direction `PlayerData.Direction`; scene `PlayerData.currentGameSceneId`.
- **Resources (GameRes)**: `PlayerData.GetRes("energy" | "insanity" | "money" | "stamina" | "happiness")`;
  max energy: `PlayerEnergyGameResSystem.GetSystem().Max`.
- **Inventories**: `PlayerData.inventory` (main), `toolBeltInventory` (tools: shovel, scalpel...). Structure:
  `Inventory.Data` is an `Item` whose `.Inventory` is the `List<Item>`. `Item.id`, `Count`, `Definition` (`ItemDef`:
  `itemGroupIds`, `type`, `itemSize`), `Item.Inventory` (nested items — e.g. organs inside the body).
- **"Overhead" items** (bodies, `ItemSize.Big` items): `PlayerData.OverheadItems`, `HasOverheadItem`,
  `HasFreeOverheadSlot`, `AddOverheadItem`, `DropOverheadItem()`.
- **Control**: `PlayerController.IsControlEnabledByType(TakenControlType)` — flags:
  `ByWork, ByUI, ByLadder, ByFlow, ByTeleport, BySleep, ByAttack, ByDeath, ByFishing, ByBuilding, BySelf, ByCinematics`.

## 3. Movement / pathfinding
- The game itself moves the player in scripted scenes (`Flow_GoTo`, `ReservoirInteractionHandler`) with:
  ```csharp
  MainGame.PlayerController.MovementComponent.StartPath(
      destination, MainGame.PlayerData.currentGameSceneId, destinationScene,
      MovementType.Recast, speed: 1.5f, "", onFinish,
      MainGame.PlayerController.PlayerLocalAreaMovement.Seeker);
  ```
  Returns `StartPathResult` (`Started`, `AlreadyAtDestinationPoint`, ...). `MovementComponent.IsMoving`, `ForceStop()`.
  `MovementType`: `Direct, Recast, GDGraph, WorldZone` (A* Pathfinding Project, the scene's Recast graph).
- When **working** on an object, `PlayerWorkComponent` already walks by itself to the object's *dock point*
  (`PlayerLocalAreaMovement.StartMovement`) — the "last meter" is handled by the game.
- Dock points: `Wgo.TryGetDockPointForWorker(findNearest, pos)` (view) / `WgoData.GetDockPointDataWorldPosition`.

## 4. World objects (WGO) and items on the ground
- Current scene: `MainGame.WorldData.GetGameSceneDataById(sceneId)` → `wgoDataList`, `droppedItems` (`DropData`).
- `WgoData`: `id` (def), `UniqueId`, `Position`, `WorldId`, `Inventory`, `CraftComponent`, `Worker`, `CustomTag`,
  `Definition` (`WGODef`: `interactionType`, `wgoGroup`, `toolAction`, ...).
- Ready-made lookups: `WorldData.GetWgoDataList(defId)`, `GetWgoDataListByGroup(group)`, `GetWgoDataByCustomTag(tag)`.
- View (GameObject) of a `WgoData`: `GameScene.GetWgoViewGlobal(uniqueId)` → `Wgo.InteractionHandler`.
- Relevant `WGODef.InteractionType` values: `Autopsy(11)`, `Grave(7)`, `Embalm(28)`, `Crematorium(42)`,
  `RiverDump(47)`, `Craft(2)`, `Work(1)`, `Chest(6)`, `Garden(13)`, `Zombie(15)`.
- Big items on the ground (bodies) are `DropView` with `BigDropInteractionHandler`: `Interact()` removes the drop and
  calls `PlayerData.AddOverheadItem(item)` (= picking up the body).

## 5. Interaction (what happens when the player presses keys)
- `PlayerInteractionComponent` picks the target by the collider in front: `WgoUnderInteraction` / `BigDropUnderInteraction`.
- `PlayerInputHandler`:
  - `GameKey.Interaction` (**E**) → the WGO's quest events, otherwise `handler.HasInteraction()` → `handler.Interact(player)`.
  - `GameKey.Action` (**hold**) → `WorkPlayerState` → `PlayerWorkComponent.TryStartInteraction/UpdateInteraction`
    (walks to the dock point, picks the tool, spends energy, advances the recipe).
- **Game input** = `LazyBearTechnology.LazyInput` (MonoBehaviour). In `Update()` it clears and refills the private
  lists `pressedKeys`/`holdedKeys`; `GetKeyDown/GetKey(GameKey)` only query those lists; if
  `IsInputActive()` is false, `Update` returns early (the game blocked input).
  ⇒ **Virtual input**: Postfix on `LazyInput.Update` appending the bot's keys to the lists. The game runs
  exactly the same code as when the player presses the key (energy, tool, animations, rules).

## 6. Recipes (craft)
- `WgoData.CraftComponent`: `CraftsIn` (available recipes), `IsStarted`, `Status`, `CurrentCraftElement`,
  `CraftElementsQueue`, `AddToQueue(CraftElement, addToQueueTop)`, `TryContinueFromQueue()`, `Cancel()`.
- The craft windows use **data** classes that work without opening the UI:
  `new UICraftSelectionWindowData(wgoData, craftDef, onAddToQueue, onStartCraft)` computes the default items
  (`CurrentNeedItems`), `ParamsData` and `CanStartCraft`; `OnCraftStarted()` calls the callback just like the button.
  The handler then does `new CraftElement(id, count, needItems, params)` → `AddToQueue` → `TryContinueFromQueue`.
- Manual progress: the player **holds Action** near the object (section 5).

## 7. Bodies (MVP "process bodies")
- Body = `Item` whose `itemGroupIds` contain `"body"`; a zombie body also has `"zombie"`; exclude `ItemType.Demon`.
  Organs/pockets are in `bodyItem.Inventory` (by `ItemType`). Definition: `BodyDef` (`linkedBodyItemId`).
- **Picking up a body from the ground**: interact with the `DropView` (section 4).
- **Autopsy table** (`AutopsyInteractionHandler`):
  - with a body overhead and an empty table → `Interact` inserts the body (`GlobalEvent PlayerInsertBodyToAutopsy`);
  - otherwise it opens `UIAutopsyWindow`. Logic in `UIAutopsyWindowData`:
    - extract organ: `GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.ExtractOrgan, organId)` → craft selection → queue;
    - extract from a pocket: `AutopsyTypeCraft.PocketExtract`;
    - **take the body off**: removes it from the table and `AddOverheadItem` (key `GameKey.ExtractBody`).
  - Table busy with a recipe in progress: `CraftComponent.IsStarted` → hold Action to work.
- **Graves** (ids in `GameConsts`): `grave_ground` (plot), `grave_empty` (open grave), `grave_body`
  (with a body), `grave_exhume`. Inserting a body into `grave_empty` plays the sound `oh_corpse_grave_drop`; the exact
  burial recipes will come from the dump. `UIGraveWindow` shows quality (skulls) and exhumation.
- Other destinations: `EmbalmInteractionHandler`, `CrematoriumInteractionHandler`, `RiverDumpInteractionHandler`
  (all of them consume the body carried overhead).
- Zombies can be **workers** at craft stations (`CraftInteractionHandler` + `ZombieSystemData`) —
  the bot does not touch zombies in the MVP.

## 8. States that PAUSE the bot
`!IsInGame` · `MainGame.IsGamePaused` · `LazyWindowsStackController.ActiveWindow != null` (any window) ·
`TakenControlType` disabled for `ByCinematics / ByFlow / ByUI / BySleep / ByTeleport / ByDeath / ByBuilding` ·
`!LazyInput.IsInputActive()`. (Implemented in `GameApi.GetBlockReason()`.)

## 9. Clock
`EnvironmentEngine.Instance.timeOfDay` (0..1), events `OnTimeOfDayChangedEvent`, `OnNewDayStarted`;
`GameSave.environmentData.Day` and `CurrentDayNumber` (day of the week). `gameplayDayInMinutes` (default 5).

---

## 10. Proposed `GameApi` signatures (for approval)

Already implemented (read-only): `IsMainGameReady`, `IsInGame`, `GetGameVersion()`, `GetBlockReason()`,
`GetSceneId()`, `GetPlayerPosition()`, `GetEnergy()`, `GetEnergyMax()`, `GetPlayerRes()`, `GetOverheadItemIds()`,
`CountPlayerItem()`, `GetTimeOfDay()`, `GetDay()`, `GetWeekDayNumber()`, `WriteDiscoveryDump()`.

Proposed for the bot (the bot never receives game types — only the mod's *handles* and DTOs):

```csharp
// Opaque handles: the bot only keeps the unique id; GameApi resolves it to WgoData/DropData on every use.
readonly struct WorldObjectRef { string Uid; string DefId; Vector3 Position; ObjectKind Kind; }
enum ObjectKind { AutopsyTable, Grave, EmbalmTable, Crematorium, RiverDump, CraftStation, Chest, Other }
readonly struct GroundItemRef { string Uid; string ItemId; Vector3 Position; bool IsBody; }

// --- world queries (current scene)
IReadOnlyList<WorldObjectRef> FindObjects(ObjectKind kind);
IReadOnlyList<GroundItemRef> FindGroundBodies();                     // bodies on the ground (excludes zombie/demon)
bool TryGetObject(string uid, out WorldObjectRef obj);                // does it still exist?
string GetObjectDefId(string uid);                                    // e.g. grave_empty -> grave_body
bool ObjectHasBody(string uid);                                       // table/grave has a body inside
bool IsCraftRunning(string uid);                                      // CraftComponent.IsStarted
IReadOnlyList<string> GetBodyOrgans(string tableUid);                 // extractable organs of the body on the table
bool IsCarryingBody();                                                // overhead with group "body"

// --- movement (the game's own pathfinding)
bool MoveTo(Vector3 target, float stopDistance);                      // StartPath(Recast) with the player's Seeker
bool MoveToObject(string uid);                                        // to the object's dock point
bool IsMoving { get; }
void StopMoving();                                                    // ForceStop
float DistanceTo(Vector3 p);

// --- virtual input (same path as the keyboard; see Patches/VirtualInputPatch)
bool IsTargetUnderInteraction(string uid);                            // WgoUnderInteraction/BigDrop == target
void PressInteract();                                                 // GameKey.Interaction for 1 frame
void SetHoldAction(bool hold);                                        // holds/releases GameKey.Action (work)
void ReleaseAllVirtualKeys();

// --- composite actions equivalent to UI clicks (use the windows' data classes, without opening UI)
bool CanStartAutopsyExtract(string tableUid, string organId, out string reason);
bool StartAutopsyExtract(string tableUid, string organId);            // = click the organ + "start"
bool TakeBodyFromTable(string tableUid);                              // = "take body out" button
bool CanStartCraft(string uid, string craftId, out string reason);
bool StartCraft(string uid, string craftId, int count = 1);           // = craft window + "start"
```

Rules for all methods: check `GetBlockReason()==null` before acting, validate distance
(act only near the target, like the player), never create items nor touch the save, and log once if the game changed.

## 7b. What the F10 dump showed (user's save, game 1.007)
- Scene `RuinedTemple` contains the outdoor area **and** the morgue interior; they are connected by doors
  (`tp_RT_morgue_exit` inside / `tp_RT_morgue_enter` outside, CustomInteraction = teleport).
- **Bodies arrive on pallets** `pallet_corpse_1/2` (group `morgue_pallets`, CustomInteraction, up to 2 bodies),
  not on the ground. Pick up = E on the pallet with empty hands.
- Body = item `body_corpse`, groups `body, corpse, overhead`, size Big. Children: `body_certificate:N`
  (group `burial_reward`), organs `skin_*, bones_*, skull_*, heart_*, brain_*, guts_*` and `flesh/blood/fat`.
  Zombies: `body_zombie` / `body_wild_zombie` (without `corpse`).
- Tables `autopsy_table_1/2`: recipes `extract_<organ>` **with no required items**, the table's default tool (surgical kit).
- `crematorium_1` (~12 m from the tables): E with a body overhead inserts it and starts `burn_crematorium_1` (automatic, no work);
  when it finishes it becomes `ReadyToFinishAutoCraft` and the **Action** key collects the result.
- `embalm_table_1`: automatic `embalm_*` recipes.
- The graveyard is outside the morgue; the save's 12 graves are `grave_ground` (occupied, with lid/fence). No
  `grave_empty` at the moment → burial postponed to 0.3 (go through the door + empty-grave recipe; the dump now
  includes `defsOfInterest` with the definitions of `grave_*`, pallets and doors).

## 11. Plan for the MVP "process bodies" (v1)
Loop per body, with a safe end (energy < `MinEnergy`, no body, no free table, no tool → stop):
**Implemented in 0.2.0 (default destination = crematorium, chosen by the user):**
pallet → free table → extract organs → take the body off → crematorium (E) → collect ashes (Action) when ready.
The original plan below still applies to the grave (0.3).

1. If not carrying a body: find the nearest body on the ground → walk → **E** (picks it up).
2. Find an empty autopsy table → walk → **E** (inserts the body).
3. For each organ enabled in the config (e.g. `ExtractOrgans = all | none | list`) → start the extraction →
   **hold Action** until the recipe finishes (energy checked every tick).
4. Take the body off the table → configurable destination: `Grave` (free `grave_empty`) / `RiverDump` / `Crematorium` /
   `LeaveOnTable` → walk → **E** / burial recipe with **Action**.
5. Repeat. The overlay shows the current step; F8 interrupts and releases everything.

**Missing data (will come from the F10 dump near the morgue/graveyard):** table ids, burial recipe in
`grave_empty` and its required items, where bodies arrive, required tools (`customItemTypeAction`).

## 13. Grave (0.3.0) — actual definitions from game 1.007 (F10 dump, `defsOfInterest`)
- `grave_empty`: `CustomInteraction`; hint `hint_place_body`; condition `HasPlayerOvrhdItemByGrp("body") && !wild_zombie && !zombie`;
  execution `InsertOvrhdItem()` + `ChangeWgo("grave_body")` → **not a recipe**: E with a body overhead places the body.
- `grave_body`, `grave_exhume`, `grave_empty_test`, `grave_empty_place`: `Work`, tool `Shovel` (hold Action).
- `grave_ground` (the save's closed graves): inventory = `body_corpse` + `grave_top_*` / `grave_bot_*`; `interactionType` Grave.
- Graveyard at ~(30..37, 17..19); the test save had no `grave_empty` (12 occupied `grave_ground`).
- Bot: Bury = PressInteract with a body (done when the player is no longer carrying it); FillGrave = SetHoldAction(true) until the
  object's id stops being `grave_body` (90 s timeout).
- `grave_empty_place` (Work, Shovel) is the grave **placed** with the builder: build recipe `grave_empty_place_p` in
  `builder_graveyard` (area `temple_graveyard_module_area_grave`) → working it with the shovel turns it into `grave_empty`. Seen in the data
  (`resources.assets`) and confirmed in the 0.3.15 dump (`defsOfInterest`): `grave_empty_place` hp 4 → `replaceToWgoOnDie`
  `grave_empty`; `grave_body` hp 3 → `grave_ground` (on death: `DropBurialRewards()`, `DecPPar("cur_bodies_count", 1)`);
  `grave_exhume` hp 4 → `grave_empty` (`DropItemByGroup("body")`). Bot 0.3.14: DigGrave = hold Action until the id is no
  longer `grave_empty_place`.
- When the object is swapped, the game's aim moves to the new object: check the id BEFORE the aim guard.

## 14. Work spot chosen by the game (0.3.13, IL of 1.007.1)
- Holding Action → `PlayerWorkComponent.TryStartInteraction` → `FindWgoToWork([WgoUnderInteraction])` →
  `FindNearestDockPoint(pos, dir, ignoreDir, list)`: the object's active points with `CanWorkOn` and
  `PlayerLocalAreaMovement.IsReachable(point)` (local 4.4 m GridGraph around the player + `PlayerColliderTester`);
  cost = A* path length + angle × 0.00267. If the player is not on the spot (`IsOnWorkingSpot`: < 0.1 m), the game walks
  (`StartMovement`) or teleports (`SetPosition`) to it and `AlignPlayerToDockPoint` turns the player.
- Consequence: the "off-navmesh"/"tight" spot from our `TryGetStandSpot` does not match the game's; the bot remembers
  where the game put it when the work progresses (`workSpots`) and uses that spot afterwards.
- Player facing: `PlayerData.Direction` (Vector2).

## 15. Insanity and sleep (0.3.15)
- Max energy = 100 − insanity (dumps: 93.8+6.2; 49.1+50.9). Near 80 the game does not let autopsies/graves progress.
- `EnergySystem.TrackTimeWithoutSleep`: `PlayerData.energySystem.timeWithoutSleep` (days) ≥ 2 → perk
  `lack_of_sleep_debuff` in `MainGame.Instance.GameSave.perkSystemData` (`HasPerk`). With it, energy spent turns into
  insanity (seen: 11 → 51 in one body). Sleeping until energy is full removes the debuff.
- Exact rules (IL, game 1.007.1, checked in 0.3.22):
  - `PlayerEnergyGameResSystem.Add(v)`: with v < 0 and the debuff → `AddRes("insanity", -v / 2)` — **half** of the energy spent turns into insanity.
  - `EnergySystem.StartSleeping`: with full energy and **without** the debuff the game refuses ("not required"); with the debuff it sleeps
    even at full energy. On start: `timeWithoutSleep = 0`, time ×50 (`SetTimeSpeedMultiplier(50)`), control
    taken (`TakenControlType.BySleep` → the bot pauses as "sleeping").
  - `RestoreEnergyWhileSleeping`: energy +400×Δday while not full; when it fills up with the debuff → `DeactivateLackOfSleep`
    (removes the perk, `timeWithoutSleep = 0`) and **insanity −20**; wakes up when energy is full and
    `remainingSleepTime` has run out. `StopSleeping` saves the game (except with `sleepWithoutSavingGame`).
  - `timeWithoutSleep` advances at the same rate as `timeOfDay` (dumps 000209 → 000607: +0.4535 on both, in 3 min 58 s of real time).
- Test 0.3.18 (dumps 20260930-142243 → 142433): yard, day 139 18:23, 2.03 days awake, Lack of sleep, energy 93.8/93.8,
  insanity 6.15 → the bot went through the "home enter" door to `bed` (`customTag bed_home`), slept, woke up ~23h without
  the debuff, came back through the "home basement enter" door and continued the body on the table (4 extractions).
  Afterwards: day 140 04:08, 0.21 days awake, energy 28/92.8, insanity 7.2 (autopsy extractions add insanity on their
  own), inventory +1 bones, skull, heart, flesh.

## 16. Player movement and physics (0.3.33, IL of 1.007.1 + Player.log)
- The bot walks with `MovementComponent.StartPath(..., MovementType.Recast, 3.3, ..., PlayerLocalAreaMovement.Seeker)`.
  `StartPath` calls `PlayerController.OnPathStart` synchronously → `PlayerPhysicalBody.SetNonKinematicFlag(ByMovementComponent, false)`
  (the flags are a `MultiFlagAND`: the body is dynamic only while every flag is true) → kinematic body.
- Every step goes through `PlayerController.set_MovablePosition` → `PlayerPhysicalBody.MoveByPosition`, which only moves a
  **kinematic** body. With a dynamic body it logs `Trying to move non-static RB by position` (Player.log, every frame) and
  returns — the `MovementComponent` stays in "moving" forever: no failure, no progress.
- `PlayerLocalAreaMovement.StopMovement(force: true)` sets `SetNonKinematicFlag(ByMovementComponent, true)` unconditionally
  (same flag as the bot's path). It is called by `PlayerWorkComponent.StopInteraction`, which `WorkPlayerState.OnExit` always
  calls (also `WorkPlayerState.Update` when the state stops being active with work in progress).
- `WorkPlayerState.IsActive` = Action held **or** `ToolComponent.IsControlTakenByAnimation`: after Action is released the
  state lasts until the tool motion ends. Current state: `PlayerController.Ssm.CurState` (type name `WorkPlayerState`).
- Seen in 0.3.32 (Player.log of 2026-10-02): energy low mid-extraction → the bot aborted and started the walk to the door
  in the same frame → `SetPauseState:[ByWork] isPaused:[False]`, `SSM.ExitState: WorkPlayerState` → ~2,700 ×
  `Trying to move non-static RB by position` (45 s × 60 fps) → walk timeout. 4 out of 4 trips to the bed.
  Fix (0.3.33): wait until the player leaves `WorkPlayerState` before `StartPath`; during a walk, a non-kinematic body for
  0.5 s = frozen → stop and start the path again.
- Real path length: `MovementComponent.SetOnPathLengthReady(Action<float>)`, called once in `OnPathCalculated`
  (`Path.GetTotalLength()`), a few frames after `StartPath` (one-shot; cleared by `ForceStop`/failures). From the mine door
  (0.9, −6.4) to the house door (−1.9, 22.2) — 29 m in a straight line — the game walks through forest, forest post, village,
  crossroads and graveyard.
- Player.log: `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Player.log` (previous session:
  `Player-prev.log`). It has the game's Unity messages interleaved with the BepInEx lines (`LogOutput.log` does not).

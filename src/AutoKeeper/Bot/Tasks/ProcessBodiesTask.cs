using System;
using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot.Tasks
{
    /// <summary>
    /// "Process bodies" routine (real morgue layout, game dump 1.007):
    ///   pallet (pallet_corpse) or ground → free autopsy table → extract organs and "Others" (config) → take body out →
    ///   destination: crematorium (default), leave on the table, or empty grave (experimental).
    /// Each Tick picks ONE goal from the world state (replans on its own after a pause/abort)
    /// and runs it in short steps: walk → aim → press E / start recipe → hold Action until done.
    /// If the goal is in another area (behind doors), the goal becomes "use the next door" on the path.
    /// Uses only GameApi; no mod teleporting, spawning or save editing.
    /// </summary>
    internal sealed class ProcessBodiesTask : ITask
    {
        private enum Goal
        {
            None,
            PickUpBody,
            PutOnTable,
            ExtractOrgan,
            ExtractPocket,
            TakeBody,
            Bury,
            PickUpFromPallet,
            Cremate,
            CollectCrematorium,
            UseDoor,
            InspectCrematorium,
            DepositChest,
            FillGrave,
            ParkOnPallet,
            DigGrave,
            Sleep,
        }

        private enum Step
        {
            Plan,
            Move,
            Aim,
            AwaitInteract,
            Act,
            Work,
        }

        private enum DecisionKind
        {
            None,
            Act,
            Wait,
            Fail,
        }

        /// <summary>Candidate object with its region and the cost (m) to reach it from the player.</summary>
        private readonly struct Candidate
        {
            public readonly WorldObjectRef Obj;
            public readonly uint Area;
            public readonly float Cost;

            public Candidate(WorldObjectRef obj, uint area, float cost)
            {
                Obj = obj;
                Area = area;
                Cost = cost;
            }
        }

        /// <summary>World snapshot used for one decision (only data copied from GameApi).</summary>
        private sealed class WorldView
        {
            public uint Here;
            public Dictionary<uint, AreaRoute> Routes;
            public List<Candidate> Tables;
            public List<Candidate> Pallets;
            public List<Candidate> Crematoriums;
            public List<Candidate> Graves;
            public List<Candidate> GravePlaces;
            public List<GroundItemRef> GroundBodies;
            public List<Decision> RemoteBodies;   // bodies on the ground outside local range (other areas), cheapest to most expensive
            public int UnroutableBodies;          // bodies on the ground in a region with no known path (log only)
            public string UnroutableAreas;
            public List<Candidate> Chests;
            public List<Candidate> GraveBodies;
        }

        /// <summary>What to do now (no side effects; PlanNext applies it).</summary>
        private struct Decision
        {
            public DecisionKind Kind;
            public Goal Goal;
            public string Uid;
            public Vector3 Pos;
            public uint Area;
            public bool Ground;
            public string Body;
            public string Part;
            public string Text;   // reason (Wait/Fail)
        }

        private const float NearEnough = 2.2f;        // distance from the stop point that counts as "arrived"
        private const float AimTimeout = 2.5f;
        private const float InteractTimeout = 3f;
        private const float MisaimTimeout = 6f;       // s spent trying to re-aim at the target before giving up
        private const float ChestPreferSlack = 15f;   // extra m accepted to walk to the chest that already holds the same items
        private const float DoorTimeout = 4f;
        private const int MaxMoveRetries = 2;
        private const float WorkStateWaitMax = 3f;   // s waiting for the game's work animation to end before walking
        private const float StallSeconds = 8f;       // s without leaving the spot (path active) = stuck
        private const float FrozenBodySeconds = 0.5f; // s with a non-kinematic body on an active path = the game will not move it
        private const int MaxBodyFixes = 3;
        private const float MaxWalkSeconds = 300f;   // hard cap for a walk that keeps making progress
        private const float DoorBanWalkSeconds = 60f;   // door left out of the routes after a failed walk to it
        private const float DoorBanUseSeconds = 120f;   // ... after using it led nowhere
        private const float SameRoomDistance = 20f;  // object with no known region but nearby = same room
        private const float IdleRecheckSeconds = 2f;

        private readonly Settings settings;
        private readonly Navigator nav;

        // Memory across goals (does not depend on game types).
        private readonly HashSet<string> autopsyDone = new HashSet<string>();        // bodies that have already been through the table
        private readonly HashSet<string> failedParts = new HashSet<string>();         // "body|item" the game refused
        private readonly HashSet<string> masterySkipLogged = new HashSet<string>();   // "body|item" skipped due to mastery (logged once)
        private int bodiesFinished;

        // Crematorium: check on arrival; chest: only what the bot collected (extraction/crematorium).
        private readonly HashSet<string> checkedCrem = new HashSet<string>();
        private readonly HashSet<string> failedChests = new HashSet<string>();
        private readonly HashSet<string> failedPallets = new HashSet<string>();      // pallets that refused a body (full)
        private readonly HashSet<string> parkedPallets = new HashSet<string>();      // pallets where the bot left an already autopsied body
        private readonly Dictionary<string, int> ledger = new Dictionary<string, int>();   // itemId → units collected by the bot
        private Dictionary<string, int> invBefore;                                          // inventory snapshot before extracting/collecting
        private string workItemId;                                                          // item expected from the current collection (null = any)

        // The game delivers what was extracted/collected a moment LATER (the item leaves the object and flies to the player):
        // each collection becomes a pending credit that is only added to the ledger after a few seconds.
        private sealed class PendingCredit
        {
            public Dictionary<string, int> Before;
            public string ItemId;   // only this item (extraction); null = any new item (crematorium)
            public int Cap;         // max units (extraction = 1); 0 = no limit
            public float Due;
        }
        private readonly List<PendingCredit> pendingCredits = new List<PendingCredit>();
        private const float CreditDelay = 2f;
        private const float CreditMaxAge = 20f;   // stale pending credit (bot stopped/paused for a long time): discard

        // Sleep: go to sleep in the home bed when Lack of sleep hits ([Bot] OnLackOfSleep = Sleep).
        private bool sleepAnnounced;
        private bool sleepForEnergy;   // going to bed because the food ran out (not Lack of sleep)
        private string lastIdle;   // last "nothing to do" summary written to the log
        private int sleepTries;
        private int doorForgives;   // times the door bans were lifted to find the bed (per trip to the bed)
        private const int MaxDoorForgives = 2;
        private const int MaxSleepTries = 3;
        private const float SleepStartTimeout = 6f;
        private uint prevHere;
        private readonly List<Vector3> buriedSpots = new List<Vector3>();   // graves where the bot placed a body that still need to be filled in
        private const float FillTimeout = 90f;
        private const float SameSpot = 1.6f;
        private int seenGeneration = -1;
        private bool chestWarned;
        private int freeAfterDeposit = int.MaxValue;   // free slots right after the last "store in the chest"

        // Work spot the GAME ITSELF used (it moves the player to its own spot while Action is held). Without this the bot
        // walked back to the spot it had picked before each organ and the game moved it again to the other side of the table.
        private readonly Dictionary<string, WorkSpot> workSpots = new Dictionary<string, WorkSpot>();

        private struct WorkSpot
        {
            public Vector3 Pos;
            public Vector2 Facing;
        }

        // Current goal.
        private Goal goal;
        private Step step;
        private string targetUid;
        private bool targetIsGround;
        private Vector3 targetPos;
        private Vector3 standSpot;
        private Vector2 standFacing;
        private string partId;
        private string bodyUid;
        private float stepStartedAt;
        private float moveTimeout;
        private int moveRetries;
        private bool moveStartPending;   // walk waits for the game's work state to end (see GameApi.IsPlayerInWorkState)
        private float straightDist;
        private bool pathLengthKnown;
        private Vector3 progressPos;
        private float progressAt;
        private float frozenSince = -1f;
        private int bodyFixes;
        private int lastProgress;
        private float lastProgressAt;
        private float misaimSince = -1f;   // since when the game has been aiming at another object during work (-1 = aiming correctly)
        private bool planFailed;
        private bool nudged;
        private string travelPurpose;   // for the panel: what the bot will do on the other side
        private int travelDoors;

        // Idle: do not redo the full planning every tick.
        private float idleCheckedAt = -999f;
        private string idleReason;

        public ProcessBodiesTask(Settings settings, Navigator nav)
        {
            this.settings = settings;
            this.nav = nav;
        }

        public string Name => Lang.T("Processar corpos", "Process bodies");

        public string Status { get; private set; } = Lang.T("aguardando", "waiting");

        private static float Now => Time.unscaledTime;

        private float Radius => settings.SearchRadius.Value;

        // ------------------------------------------------------------------ ITask

        public bool CanRun(out string reason)
        {
            SettleCredits(); // also while the task is idle (e.g. right after collecting the crematorium)
            if (!settings.BodiesEnabled.Value)
            {
                reason = Lang.T("desativada na config", "disabled in config");
                return false;
            }
            if (GameApi.IsCarryingBody() && !GameApi.CarriedBodyIsPlain())
            {
                reason = Lang.T("carregando corpo de zumbi/demônio (fora do MVP)", "carrying a zombie/demon body (outside the MVP)");
                return false;
            }
            if (!GameApi.IsCarryingBody() && GameApi.IsCarryingAnything())
            {
                reason = Lang.T("mãos ocupadas com outro item", "hands busy with another item");
                return false;
            }
            if (idleReason != null && Now - idleCheckedAt < IdleRecheckSeconds)
            {
                reason = idleReason;
                return false;
            }
            if (PlanNext(dryRun: true))
            {
                idleReason = null;
                reason = null;
                return true;
            }
            idleCheckedAt = Now;
            idleReason = settings.TravelEnabled.Value
                ? Lang.T("sem corpos para processar (nem atrás das portas)", "no bodies to process (not even behind the doors)")
                : Lang.T("sem corpos para processar nesta área (\"Ir até o trabalho pelas portas\" está desligado)",
                    "no bodies to process in this area (\"Walk to the work (through doors)\" is off)");
            reason = idleReason;
            return false;
        }

        public TaskResult Tick()
        {
            SettleCredits();
            if (!WatchChests())
            {
                return TaskResult.Failed;
            }
            switch (step)
            {
                case Step.Plan:
                    planFailed = false;
                    bool planned = PlanNext(dryRun: false);
                    if (planFailed)
                    {
                        return TaskResult.Failed;
                    }
                    if (!planned)
                    {
                        Status = bodiesFinished > 0
                            ? Lang.T($"nada mais a fazer ({bodiesFinished} corpo(s) concluído(s))", $"nothing else to do ({bodiesFinished} body(ies) done)")
                            : Lang.T("nada a fazer", "nothing to do");
                        return TaskResult.Succeeded;
                    }
                    return TaskResult.Running;
                case Step.Move:
                    return TickMove();
                case Step.Aim:
                    return TickAim();
                case Step.AwaitInteract:
                    return TickAwaitInteract();
                case Step.Act:
                    return TickAct();
                case Step.Work:
                    return TickWork();
                default:
                    return Fail(Lang.T("passo desconhecido", "unknown step"));
            }
        }

        public void ResetMemory()
        {
            Abort();
            autopsyDone.Clear();
            failedParts.Clear();
            masterySkipLogged.Clear();
            bodiesFinished = 0;
            checkedCrem.Clear();
            failedChests.Clear();
            failedPallets.Clear();
            parkedPallets.Clear();
            ledger.Clear();
            pendingCredits.Clear();
            workItemId = null;
            sleepAnnounced = false;
            lastIdle = null;
            sleepTries = 0;
            doorForgives = 0;
            buriedSpots.Clear();
            chestWatch.Clear();
            prevHere = 0;
            seenGeneration = -1;
            chestWatchGen = -1;
            chestWarned = false;
            freeAfterDeposit = int.MaxValue;
            workSpots.Clear();
            misaimSince = -1f;
            invBefore = null;
            idleCheckedAt = -999f;
            idleReason = null;
            Status = "-";
        }

        public void Abort()
        {
            // Interrupted (eating, pause, turning off) mid-collection: the recipe may have finished right at that moment.
            if (invBefore != null && (step == Step.Work || step == Step.AwaitInteract || step == Step.Act))
            {
                QueueCredit();
            }
            invBefore = null;
            GameApi.ReleaseAllVirtualKeys();
            GameApi.StopMoving();
            ResetGoal();
            Status = Lang.T("interrompido", "interrupted");
        }

        // ------------------------------------------------------------------ chest watch

        private readonly Dictionary<string, int> chestWatch = new Dictionary<string, int>();
        private float chestWatchAt = -10f;
        private int chestWatchGen = -1;

        /// <summary>
        /// Watch: the bot never takes items out of a chest. If a nearby chest loses items while the bot is on, it turns off
        /// at once and logs what the bot was doing (the Action key on a chest = the game's "take all").
        /// </summary>
        private bool WatchChests()
        {
            if (Now - chestWatchAt < 0.25f)
            {
                return true;
            }
            chestWatchAt = Now;
            if (chestWatchGen != nav.Generation)
            {
                chestWatchGen = nav.Generation; // bot turned back on: the player may have touched the chests while it was off
                chestWatch.Clear();
            }
            var seen = new HashSet<string>();
            foreach (WorldObjectRef c in GameApi.FindObjects(ObjectKind.Chest, 8f))
            {
                int total = GameApi.ChestItemTotal(c.Uid);
                if (total < 0)
                {
                    continue;
                }
                seen.Add(c.Uid);
                if (chestWatch.TryGetValue(c.Uid, out int before) && total < before)
                {
                    GameApi.ReleaseAllVirtualKeys();
                    GameApi.StopMoving();
                    string msg = Lang.T(
                        $"VIGIA: o baú {c.DefId} perdeu {before - total} item(ns) com o bot ligado — objetivo {GoalText()}, passo {step}, "
                        + $"alvo do jogo {GameApi.DescribeInteractionTarget()}, pos {GameApi.GetPlayerPosition()}. Bot desligado por segurança.",
                        $"CHEST WATCH: chest {c.DefId} lost {before - total} item(s) while the bot was on — goal {GoalText()}, step {step}, "
                        + $"game target {GameApi.DescribeInteractionTarget()}, pos {GameApi.GetPlayerPosition()}. Bot turned off for safety.");
                    ModLog.Warn(msg);
                    chestWatch.Clear();
                    ResetGoal();
                    Status = Lang.T("vigia do baú: desligado", "chest watch: turned off");
                    return false;
                }
                chestWatch[c.Uid] = total;
            }
            foreach (string gone in chestWatch.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                chestWatch.Remove(gone); // out of range: forget it (measure again when back nearby)
            }
            return true;
        }

        // ------------------------------------------------------------------ world snapshot

        private WorldView Look()
        {
            var w = new WorldView
            {
                Here = GameApi.GetPlayerNavArea(),
            };
            if (seenGeneration != nav.Generation)
            {
                seenGeneration = nav.Generation;
                checkedCrem.Clear();   // bot turned back on: check the crematorium again
                failedChests.Clear();
                failedPallets.Clear();
                chestWarned = false;
                freeAfterDeposit = int.MaxValue;
            }
            if (w.Here != 0 && w.Here != prevHere)
            {
                if (prevHere != 0)
                {
                    checkedCrem.Clear();   // arrived in another area: check the crematorium again
                }
                prevHere = w.Here;
            }
            w.Routes = w.Here != 0 ? nav.ReachableAreas(settings.TravelEnabled.Value) : new Dictionary<uint, AreaRoute>();
            w.Tables = Collect(ObjectKind.AutopsyTable, w);
            w.Pallets = Collect(ObjectKind.MorguePallet, w);
            parkedPallets.RemoveWhere(uid => !GameApi.ObjectHasBody(uid)); // pallet emptied (by the bot or by the player)
            w.Crematoriums = Collect(ObjectKind.Crematorium, w);
            w.Graves = settings.Destination.Value == BodyDestination.Grave ? Collect(ObjectKind.EmptyGrave, w) : new List<Candidate>();
            w.GravePlaces = settings.Destination.Value == BodyDestination.Grave && settings.DigGraves.Value && w.Graves.Count == 0
                ? Collect(ObjectKind.GravePlace, w)
                : new List<Candidate>();
            bool wantsChest = settings.UseChest.Value && LedgerTotal() > 0 && GameApi.PlayerFreeSlots() < settings.ChestFreeSlots.Value;
            w.Chests = wantsChest ? Collect(ObjectKind.Chest, w) : new List<Candidate>();
            w.GraveBodies = settings.Destination.Value == BodyDestination.Grave && buriedSpots.Count > 0
                ? Collect(ObjectKind.GraveBody, w).Where(c => buriedSpots.Any(sp => Vector3.Distance(sp, c.Obj.Position) < SameSpot)).ToList()
                : new List<Candidate>();
            w.GroundBodies = GameApi.FindGroundBodies(Radius)
                .Where(b => w.Here == 0 || GameApi.GetNavArea(b.Position) == w.Here)
                .ToList();
            w.RemoteBodies = CollectRemoteBodies(w);
            return w;
        }

        /// <summary>Why the bot has nothing to do: written to the log file once per change (diagnosing an idle bot).</summary>
        private void LogIdle(WorldView w)
        {
            int tablesBusy = w.Tables.Count(t => GameApi.ObjectHasBody(t.Obj.Uid));
            int palletsBusy = w.Pallets.Count(p => GameApi.ObjectHasBody(p.Obj.Uid));
            string crem = w.Crematoriums.Count == 0
                ? Lang.T("não achado", "not found")
                : GameApi.GetCraftState(w.Crematoriums[0].Obj.Uid).ToString();
            string s = Lang.T(
                $"Corpos: nada a fazer agora — mesas ocupadas {tablesBusy}/{w.Tables.Count}, paletes ocupados {palletsBusy}/{w.Pallets.Count}, "
                + $"crematório {crem}, corpos no chão: {w.GroundBodies.Count} aqui e {w.RemoteBodies.Count} em outras áreas"
                + (w.UnroutableBodies > 0 ? $", {w.UnroutableBodies} sem caminho (área {w.UnroutableAreas}; aqui {w.Here})" : "")
                + $"; áreas alcançáveis: {w.Routes.Count}",
                $"Bodies: nothing to do now — tables in use {tablesBusy}/{w.Tables.Count}, pallets in use {palletsBusy}/{w.Pallets.Count}, "
                + $"crematorium {crem}, ground bodies: {w.GroundBodies.Count} here and {w.RemoteBodies.Count} in other areas"
                + (w.UnroutableBodies > 0 ? $", {w.UnroutableBodies} with no path (area {w.UnroutableAreas}; here {w.Here})" : "")
                + $"; reachable areas: {w.Routes.Count}");
            if (s != lastIdle)
            {
                lastIdle = s;
                ModLog.Detail(s);
            }
        }

        /// <summary>Bodies on the ground in any reachable area (the whole scene is known), except those already seen nearby.</summary>
        private List<Decision> CollectRemoteBodies(WorldView w)
        {
            var list = new List<Decision>();
            if (!settings.FetchRemoteBodies.Value || w.Here == 0)
            {
                return list;
            }
            var near = new HashSet<string>(w.GroundBodies.Select(b => b.Uid));
            var cost = new Dictionary<string, float>();
            foreach (GroundItemRef b in GameApi.FindGroundBodies(float.MaxValue))
            {
                if (near.Contains(b.Uid))
                {
                    continue;
                }
                uint area = GameApi.GetNavArea(b.Position);
                float c;
                if (area == w.Here)
                {
                    c = GameApi.DistanceTo(b.Position);
                }
                else if (area != 0 && w.Routes.TryGetValue(area, out AreaRoute route))
                {
                    c = Navigator.CostTo(route, b.Position);
                }
                else
                {
                    w.UnroutableBodies++;   // area with no known path (no unlocked doors / off the walkable ground): skip
                    w.UnroutableAreas = w.UnroutableAreas == null ? area.ToString() : w.UnroutableAreas + "," + area;
                    continue;
                }
                cost[b.Uid] = c;
                list.Add(new Decision { Kind = DecisionKind.Act, Goal = Goal.PickUpBody, Uid = b.Uid, Pos = b.Position, Area = area, Ground = true });
            }
            list.Sort((a, b) => cost[a.Uid].CompareTo(cost[b.Uid]));
            return list;
        }

        /// <summary>Reachable objects of this kind (in this area or behind doors), cheapest to most expensive.</summary>
        private List<Candidate> Collect(ObjectKind kind, WorldView w)
        {
            var list = new List<Candidate>();
            foreach (WorldObjectRef o in GameApi.FindObjects(kind, float.MaxValue))
            {
                float straight = GameApi.DistanceTo(o.Position);
                uint area = w.Here == 0 ? 0 : nav.AreaOf(o.Uid, o.Position);
                if (w.Here != 0 && (area == 0 || !w.Routes.ContainsKey(area)))
                {
                    // Centre off the walkable regions (e.g. the crematorium): use the region of its work spot.
                    uint stand = nav.StandAreaOf(o.Uid, o.Position);
                    if (stand != 0 && w.Routes.ContainsKey(stand))
                    {
                        area = stand;
                    }
                }
                if (area != 0 && w.Routes.TryGetValue(area, out AreaRoute route))
                {
                    list.Add(new Candidate(o, area, area == w.Here ? straight : Navigator.CostTo(route, o.Position)));
                }
                else if (straight <= SameRoomDistance || (w.Here == 0 && straight <= Radius))
                {
                    // Unknown region but nearby: same room (0.2.x behavior).
                    list.Add(new Candidate(o, w.Here, straight));
                }
            }
            list.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            return list;
        }

        // ------------------------------------------------------------------ decision

        /// <summary>Picks the next goal from the world state. In dryRun it only answers whether there is something to do.</summary>
        private bool PlanNext(bool dryRun)
        {
            WorldView w = Look();
            Decision d = Decide(w);
            switch (d.Kind)
            {
                case DecisionKind.None:
                    LogIdle(w);
                    return false;
                case DecisionKind.Fail:
                    return dryRun || Fail(d.Text) != TaskResult.Failed;
                case DecisionKind.Wait:
                    if (IsRemote(w, d.Area))
                    {
                        return BeginTravel(dryRun, w, d, Lang.T("esperar: " + d.Text, "wait: " + d.Text));
                    }
                    return Wait(dryRun, d.Text);
                default:
                    if (IsRemote(w, d.Area))
                    {
                        return BeginTravel(dryRun, w, d, GoalText(d.Goal, d.Part));
                    }
                    return Begin(dryRun, d.Goal, d.Uid, d.Pos, d.Ground, d.Body, d.Part);
            }
        }

        private static bool IsRemote(WorldView w, uint area) => w.Here != 0 && area != 0 && area != w.Here && w.Routes.ContainsKey(area);

        private int LedgerTotal()
        {
            int n = 0;
            foreach (int c in ledger.Values)
            {
                n += c;
            }
            return n;
        }

        /// <summary>"First of all" rules (check the crematorium, store in the chest) on top of the normal plan.</summary>
        private Decision Decide(WorldView w)
        {
            // Sleep: with Lack of sleep and the option on, drop what it is doing (no body in hands) and go to sleep.
            if (settings.OnLackOfSleep.Value == LackOfSleepAction.Sleep)
            {
                bool lack = GameApi.HasLackOfSleep();
                bool rest = BotController.RestRequested; // out of food with low energy: sleeping refills energy
                if (!lack && !rest)
                {
                    if (sleepAnnounced)
                    {
                        sleepAnnounced = false;
                        ModLog.Info(Lang.T("Sono: descansado — voltando ao trabalho de onde parei.", "Sleep: rested — back to work where I left off."));
                    }
                    sleepTries = 0;
                    doorForgives = 0;
                }
                else if (!GameApi.IsCarryingBody())
                {
                    sleepForEnergy = !lack;
                    string why = lack ? Lang.T("Privação de Sono", "Lack of sleep") : Lang.T("sem comida e com energia baixa", "out of food with low energy");
                    if (sleepTries >= MaxSleepTries)
                    {
                        return FailWith(Lang.T($"{why}: apertei E na cama {MaxSleepTries} vezes e o personagem não dormiu — durma manualmente",
                            $"{why}: pressed E on the bed {MaxSleepTries} times and the character did not sleep — sleep manually"));
                    }
                    Candidate? bed = FirstFree(Collect(ObjectKind.Bed, w), c => true);
                    if (bed.HasValue)
                    {
                        return Act(Goal.Sleep, bed.Value, null);
                    }
                    int forgiven = doorForgives < MaxDoorForgives ? nav.ForgiveBrokenDoors() : 0;
                    if (forgiven > 0)
                    {
                        doorForgives++;
                        // A door left out after a failed walk may be the only way home: try the routes again with all doors.
                        return WaitAt(0, Lang.T($"sem rota até a cama com {forgiven} porta(s) ignorada(s) — tentando de novo com todas as portas",
                            $"no route to the bed with {forgiven} door(s) ignored — trying again with every door"));
                    }
                    return FailWith(Lang.T($"{why}, mas não achei a cama de casa alcançável — durma manualmente",
                        $"{why}, but no reachable home bed found — sleep manually"));
                }
            }

            Decision d = DecideCore(w);
            if (d.Kind == DecisionKind.None || d.Kind == DecisionKind.Fail || GameApi.IsCarryingBody())
            {
                return d;
            }

            // On arriving at the morgue: stop by the crematorium before starting (collect whatever is ready).
            if (settings.CheckCrematoriumFirst.Value && settings.Destination.Value == BodyDestination.Crematorium
                && d.Goal != Goal.CollectCrematorium)
            {
                // The state is read from afar: only go there if something is ready to collect (no visits to an empty crematorium).
                Candidate? crem = FirstFree(w.Crematoriums, c => !GameApi.HasOtherWorker(c.Obj.Uid)
                    && GameApi.GetCraftState(c.Obj.Uid) == CraftState.ReadyToCollect);
                if (crem.HasValue)
                {
                    return Act(Goal.CollectCrematorium, crem.Value, null);
                }
            }

            // Inventory almost full: take only what the bot collected to the chest. After storing, only go back to the chest if
            // the inventory filled up further (avoids a chest trip per item when the rest of the inventory is the player's).
            int freeNow = GameApi.PlayerFreeSlots();
            if (freeNow > freeAfterDeposit)
            {
                freeAfterDeposit = freeNow; // the player freed up space: follow along
            }
            if (settings.UseChest.Value && LedgerTotal() > 0 && freeNow < settings.ChestFreeSlots.Value && freeNow < freeAfterDeposit)
            {
                // The nearest chest that accepts the items. A chest that already holds these items is only preferred if it is
                // in the same area and almost as close (before, a chest 400 m away holding bones beat the empty morgue chest).
                Func<Candidate, bool> usable = c => !failedChests.Contains(c.Obj.Uid) && GameApi.ChestCanTakeAny(c.Obj.Uid, ledger.Keys);
                Candidate? nearest = FirstFree(w.Chests, usable);
                Candidate? chest = nearest;
                if (nearest.HasValue)
                {
                    Candidate n = nearest.Value;
                    chest = FirstFree(w.Chests, c => c.Area == n.Area && c.Cost <= n.Cost + ChestPreferSlack
                        && usable(c) && GameApi.ChestHasAny(c.Obj.Uid, ledger.Keys)) ?? nearest;
                }
                if (chest.HasValue)
                {
                    return Act(Goal.DepositChest, chest.Value, null);
                }
                if (!chestWarned)
                {
                    chestWarned = true;
                    ModLog.Warn(Lang.T("Baú: inventário quase cheio, mas não achei baú alcançável que aceite os itens do bot.",
                        "Chest: inventory almost full, but no reachable chest accepts the bot's items."));
                }
            }
            return d;
        }

        private Decision DecideCore(WorldView w)
        {
            HashSet<string> organs = settings.SelectedOrganTypes();
            HashSet<string> pockets = settings.SelectedPocketKinds();
            bool wantsParts = organs.Count > 0 || pockets.Count > 0;
            BodyDestination dest = settings.Destination.Value;
            List<Candidate> tables = w.Tables.Where(t => !GameApi.HasOtherWorker(t.Obj.Uid)).ToList(); // zombie/NPC working: leave it alone
            Candidate? crem = dest == BodyDestination.Crematorium ? FirstFree(w.Crematoriums, c => !GameApi.HasOtherWorker(c.Obj.Uid)) : null;
            CraftState cremState = crem.HasValue ? GameApi.GetCraftState(crem.Value.Obj.Uid) : CraftState.Unknown;
            bool cremFree = crem.HasValue && cremState == CraftState.Idle && !GameApi.ObjectHasBody(crem.Value.Obj.Uid);
            Candidate? grave = dest == BodyDestination.Grave
                ? FirstFree(w.Graves, g => !GameApi.ObjectHasBody(g.Obj.Uid) && !GameApi.IsCraftActive(g.Obj.Uid) && !GameApi.HasOtherWorker(g.Obj.Uid))
                : null;
            Candidate? freeTable = FirstFree(tables, t => !GameApi.ObjectHasBody(t.Obj.Uid) && !GameApi.IsCraftActive(t.Obj.Uid));

            // 1) Carrying a body: table (if not autopsied yet) or destination.
            if (GameApi.IsCarryingBody())
            {
                string carried = GameApi.GetCarriedBodyUid();
                if (wantsParts && carried != null && !autopsyDone.Contains(carried) && freeTable.HasValue)
                {
                    return Act(Goal.PutOnTable, freeTable.Value, carried);
                }
                switch (dest)
                {
                    case BodyDestination.Crematorium:
                        if (!crem.HasValue)
                        {
                            return FailWith(Lang.T("carregando corpo, mas não achei crematório alcançável", "carrying a body, but no reachable crematorium found"));
                        }
                        if (cremFree)
                        {
                            return Act(Goal.Cremate, crem.Value, carried);
                        }
                        // Crematorium busy and there are still new bodies: leave this one on an empty pallet and go on with the autopsies.
                        // Body not autopsied yet and no free table (e.g. brought in from outside): also goes to the pallet.
                        bool freshCarried = carried != null && !autopsyDone.Contains(carried);
                        if (wantsParts && (freshCarried || HasFreshBodyWaiting(w)))
                        {
                            Candidate? park = EmptyPallet(w);
                            if (park.HasValue)
                            {
                                return Act(Goal.ParkOnPallet, park.Value, carried);
                            }
                        }
                        return WaitAt(crem.Value.Area, Lang.T("aguardando o crematório ficar livre", "waiting for the crematorium to be free"));
                    case BodyDestination.Grave:
                        if (grave.HasValue)
                        {
                            return Act(Goal.Bury, grave.Value, carried);
                        }
                        if (w.GravePlaces.Count > 0)
                        {
                            // Can't dig with the body overhead: leave it on an empty pallet, dig, and come back for it.
                            Candidate? parkForDig = EmptyPallet(w);
                            if (parkForDig.HasValue)
                            {
                                return Act(Goal.ParkOnPallet, parkForDig.Value, carried);
                            }
                            return FailWith(Lang.T("carregando corpo e o túmulo marcado ainda precisa ser cavado — deixe o corpo num palete (não há palete vazio)",
                                "carrying a body and the marked grave still needs digging — leave the body on a pallet (no empty pallet)"));
                        }
                        return FailWith(Lang.T("carregando corpo, mas não há túmulo aberto (grave_empty) alcançável — marque um túmulo no cemitério com o construtor",
                            "carrying a body, but no reachable open grave (grave_empty) — mark a grave in the graveyard in build mode"));
                    default:
                        if (freeTable.HasValue)
                        {
                            return Act(Goal.PutOnTable, freeTable.Value, carried);
                        }
                        return FailWith(Lang.T("carregando corpo, mas não há mesa de autópsia livre", "carrying a body, but no free autopsy table"));
                }
            }

            // 1b) Body already placed in a grave: fill in the grave with the shovel ("grave_body" work).
            if (w.GraveBodies.Count > 0)
            {
                return Act(Goal.FillGrave, w.GraveBodies[0], null);
            }

            // 2) Crematorium finished: collect the result before anything else (frees it for the next body).
            if (crem.HasValue && cremState == CraftState.ReadyToCollect)
            {
                return Act(Goal.CollectCrematorium, crem.Value, null);
            }

            // 3) Tables with a plain body.
            Candidate? doneTable = null;   // table with a finished autopsy waiting for the destination
            string doneBody = null;
            foreach (Candidate t in tables)
            {
                string uid = t.Obj.Uid;
                if (!GameApi.ObjectHasPlainBody(uid))
                {
                    continue;
                }
                string body = GameApi.GetBodyUidInObject(uid);

                // 3a) Recipe in progress (extraction): work on it.
                if (GameApi.IsCraftActive(uid))
                {
                    return Act(Goal.ExtractOrgan, t, body);
                }

                // 3b) There is still a wanted organ / "Others" item to extract.
                if (wantsParts && body != null && !autopsyDone.Contains(body))
                {
                    string organ = NextPart(uid, GameApi.GetExtractableOrgans(uid, false, organs), body, pocket: false);
                    if (organ != null)
                    {
                        return Act(Goal.ExtractOrgan, t, body, organ);
                    }
                    string pocket = NextPart(uid, GameApi.GetExtractablePocketItems(uid, pockets), body, pocket: true);
                    if (pocket != null)
                    {
                        return Act(Goal.ExtractPocket, t, body, pocket);
                    }
                }

                // 3c) Autopsy done: take the body off the table only if the destination is available now.
                bool destReady = (dest == BodyDestination.Crematorium && cremFree) || (dest == BodyDestination.Grave && grave.HasValue);
                if (destReady)
                {
                    return Act(Goal.TakeBody, t, body);
                }
                if (!doneTable.HasValue)
                {
                    doneTable = t;
                    doneBody = body;
                }
            }

            // 3c') Grave destination with no open grave: dig a grave the player already marked, if a body is waiting for burial.
            if (dest == BodyDestination.Grave && !grave.HasValue && w.GravePlaces.Count > 0 && BodyAwaitingGrave(w, doneTable.HasValue, wantsParts))
            {
                Candidate? place = FirstFree(w.GravePlaces, g => !GameApi.HasOtherWorker(g.Obj.Uid));
                if (place.HasValue)
                {
                    return Act(Goal.DigGrave, place.Value, null);
                }
            }

            // 3d) Destination free: take the already autopsied bodies parked on the pallets.
            bool destFree = (dest == BodyDestination.Crematorium && cremFree) || (dest == BodyDestination.Grave && grave.HasValue);
            if (destFree && wantsParts)
            {
                foreach (Candidate pallet in w.Pallets)
                {
                    string pb = GameApi.GetBodyUidInObject(pallet.Obj.Uid);
                    if (GameApi.ObjectHasPlainBody(pallet.Obj.Uid) && (parkedPallets.Contains(pallet.Obj.Uid) || (pb != null && autopsyDone.Contains(pb))))
                    {
                        return Act(Goal.PickUpFromPallet, pallet, pb);
                    }
                }
            }

            // 3e) All tables busy with finished autopsies, crematorium busy, and there are still new bodies:
            //     take the body off the table and leave it on an empty pallet to free the table (it goes to the crematorium later).
            if (doneTable.HasValue && !freeTable.HasValue && dest == BodyDestination.Crematorium && crem.HasValue && !cremFree
                && HasFreshBodyWaiting(w) && EmptyPallet(w).HasValue)
            {
                return Act(Goal.TakeBody, doneTable.Value, doneBody);
            }

            // 4) Fetch a new body (pallet first, then ground) if there is a free table
            //    or, without autopsy, if the destination is available.
            bool canReceive = wantsParts
                ? freeTable.HasValue
                : (dest == BodyDestination.Crematorium && cremFree) || (dest == BodyDestination.Grave && grave.HasValue);
            if (!canReceive)
            {
                // No free table, but there is a body outside and an empty pallet: fetch it and leave it on the pallet (last task).
                if (wantsParts && dest == BodyDestination.Crematorium && w.RemoteBodies.Count > 0 && !w.Pallets.Any(IsFreshPalletBody)
                    && EmptyPallet(w).HasValue)
                {
                    return w.RemoteBodies[0];
                }
                return NothingToDo(w, crem, cremState);
            }
            foreach (Candidate pallet in w.Pallets)
            {
                if (IsFreshPalletBody(pallet))
                {
                    return Act(Goal.PickUpFromPallet, pallet, null);
                }
            }
            if (w.GroundBodies.Count > 0)
            {
                GroundItemRef b = w.GroundBodies[0];
                return new Decision { Kind = DecisionKind.Act, Goal = Goal.PickUpBody, Uid = b.Uid, Pos = b.Position, Area = w.Here, Ground = true };
            }
            // Last task: bodies left in other areas (goes through the doors to get there).
            if (w.RemoteBodies.Count > 0)
            {
                return w.RemoteBodies[0];
            }
            return NothingToDo(w, crem, cremState);
        }

        /// <summary>
        /// Nothing to do here: wait for the burning crematorium (if a body is pending) or, with "Wait in the morgue", go and wait
        /// in the morgue — e.g. after waking up at home, instead of standing in the house while bodies are delivered outside.
        /// </summary>
        private Decision NothingToDo(WorldView w, Candidate? crem, CraftState cremState)
        {
            Decision d = WaitForCrematorium(w, crem, cremState);
            if (d.Kind != DecisionKind.None || !settings.WaitInMorgue.Value || w.Here == 0 || w.Tables.Count == 0)
            {
                return d;
            }
            uint morgue = w.Tables[0].Area;
            return morgue != 0 && morgue != w.Here && w.Routes.ContainsKey(morgue)
                ? WaitAt(morgue, Lang.T("aguardando corpos no necrotério", "waiting for bodies in the morgue"))
                : d;
        }

        /// <summary>Body on the pallet that has not been through the table yet (those parked by the bot are left out).</summary>
        private bool IsFreshPalletBody(Candidate pallet)
        {
            if (!GameApi.ObjectHasPlainBody(pallet.Obj.Uid))
            {
                return false;
            }
            if (parkedPallets.Contains(pallet.Obj.Uid))
            {
                return false;
            }
            string b = GameApi.GetBodyUidInObject(pallet.Obj.Uid);
            return b == null || !autopsyDone.Contains(b);
        }

        /// <summary>Is there a body ready for the grave (autopsied on the table/pallet or, without autopsy, any body nearby)?</summary>
        private bool BodyAwaitingGrave(WorldView w, bool autopsiedOnTable, bool wantsParts)
        {
            if (autopsiedOnTable)
            {
                return true;
            }
            foreach (Candidate p in w.Pallets)
            {
                if (!GameApi.ObjectHasPlainBody(p.Obj.Uid))
                {
                    continue;
                }
                string b = GameApi.GetBodyUidInObject(p.Obj.Uid);
                if (!wantsParts || parkedPallets.Contains(p.Obj.Uid) || (b != null && autopsyDone.Contains(b)))
                {
                    return true;
                }
            }
            return !wantsParts && w.GroundBodies.Count > 0;
        }

        /// <summary>Is there still a body waiting for autopsy (pallet or ground)?</summary>
        private bool HasFreshBodyWaiting(WorldView w) => w.Pallets.Any(IsFreshPalletBody) || w.GroundBodies.Count > 0 || w.RemoteBodies.Count > 0;

        private Candidate? EmptyPallet(WorldView w)
            => FirstFree(w.Pallets, p => !failedPallets.Contains(p.Obj.Uid) && !GameApi.ObjectHasBody(p.Obj.Uid));

        /// <summary>
        /// Nothing to do now, but the crematorium is burning and there is still a body in the morgue (table or pallet):
        /// wait instead of ending the task.
        /// </summary>
        private Decision WaitForCrematorium(WorldView w, Candidate? crem, CraftState cremState)
        {
            if (!crem.HasValue || cremState != CraftState.Running)
            {
                return default;
            }
            bool pending = w.Tables.Any(t => GameApi.ObjectHasPlainBody(t.Obj.Uid)) || w.Pallets.Any(p => GameApi.ObjectHasPlainBody(p.Obj.Uid));
            return pending ? WaitAt(crem.Value.Area, Lang.T("aguardando o crematório ficar livre", "waiting for the crematorium to be free")) : default;
        }

        private static Candidate? FirstFree(List<Candidate> list, Func<Candidate, bool> ok)
        {
            foreach (Candidate c in list)
            {
                if (ok(c))
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>Next item to extract: skips those the game refused and, with "Skip low-chance extractions", the low-chance ones.</summary>
        private string NextPart(string tableUid, List<string> candidates, string body, bool pocket)
        {
            foreach (string id in candidates)
            {
                string key = body + "|" + id;
                if (failedParts.Contains(key))
                {
                    continue;
                }
                if (settings.RequireMastery.Value)
                {
                    ExtractMastery m = GameApi.GetExtractMastery(tableUid, id, pocket);
                    if (m.ChancePercent < settings.MinMasteryChance.Value)
                    {
                        if (masterySkipLogged.Add(key))
                        {
                            ModLog.Detail(Lang.T($"Corpos: pulando {id} — {m}, abaixo do mínimo de {settings.MinMasteryChance.Value}%",
                                $"Bodies: skipping {id} — {m}, below the minimum of {settings.MinMasteryChance.Value}%"));
                        }
                        continue;
                    }
                }
                return id;
            }
            return null;
        }

        private static Decision Act(Goal g, Candidate c, string body, string part = null)
            => new Decision { Kind = DecisionKind.Act, Goal = g, Uid = c.Obj.Uid, Pos = c.Obj.Position, Area = c.Area, Body = body, Part = part };

        private static Decision WaitAt(uint area, string why) => new Decision { Kind = DecisionKind.Wait, Area = area, Text = why };

        private static Decision FailWith(string why) => new Decision { Kind = DecisionKind.Fail, Text = why };

        /// <summary>Nothing to do now, but the task stays active (e.g. waiting for the crematorium).</summary>
        private bool Wait(bool dryRun, string why)
        {
            if (!dryRun)
            {
                if (Status != why)
                {
                    ModLog.Info(Lang.T("Corpos: " + why, "Bodies: " + why));
                }
                Status = why;
            }
            return true;
        }

        // ------------------------------------------------------------------ goal start

        /// <summary>The target is in another area: the goal is now to use the first door on the path.</summary>
        private bool BeginTravel(bool dryRun, WorldView w, Decision d, string purpose)
        {
            AreaRoute route = w.Routes[d.Area];
            if (!route.HasDoor)
            {
                return false;
            }
            if (dryRun)
            {
                return true;
            }
            DoorRef door = route.FirstDoor;
            travelPurpose = purpose;
            travelDoors = route.Doors;
            ModLog.Info(Lang.T($"Corpos: indo pela porta \"{door.Label}\" para {purpose} ({route.Doors} porta(s), ~{route.Cost:0} m)",
                $"Bodies: going through door \"{door.Label}\" to {purpose} ({route.Doors} door(s), ~{route.Cost:0} m)"));
            return Begin(false, Goal.UseDoor, door.Uid, door.Position, false, null, null);
        }

        private bool Begin(bool dryRun, Goal g, string uid, Vector3 pos, bool ground, string body, string part)
        {
            if (dryRun)
            {
                return true;
            }
            goal = g;
            targetUid = uid;
            targetPos = pos;
            targetIsGround = ground;
            bodyUid = body;
            partId = part;
            moveRetries = 0;
            if (g != Goal.UseDoor)
            {
                travelPurpose = null;
            }
            invBefore = g == Goal.ExtractOrgan || g == Goal.ExtractPocket || g == Goal.CollectCrematorium
                ? GameApi.SnapshotPlayerItems()
                : null;
            workItemId = part ?? (g == Goal.ExtractOrgan ? GameApi.GetActiveAutopsyItemId(uid) : null);
            if (g == Goal.Sleep && !sleepAnnounced)
            {
                sleepAnnounced = true;
                ModLog.Info(sleepForEnergy
                    ? Lang.T("Sono: sem comida — indo dormir na cama de casa para recuperar a energia; depois continuo de onde parei.",
                        "Sleep: out of food — going to sleep in the home bed to recover energy; then I'll pick up where I left off.")
                    : Lang.T("Sono: Privação de Sono — indo dormir na cama de casa; depois continuo de onde parei.",
                        "Sleep: Lack of sleep — going to sleep in the home bed; then I'll pick up where I left off."));
            }

            if (ground)
            {
                standSpot = GameApi.GetApproachSpot(pos, 0.8f);
                standFacing = Vector2.zero;
            }
            else if (!GameApi.TryGetStandSpot(uid, out standSpot, out standFacing))
            {
                return Fail(Lang.T($"alvo {WorldObjectRef.ShortUid(uid)} sumiu", $"target {WorldObjectRef.ShortUid(uid)} is gone")) != TaskResult.Failed;
            }
            if ((g == Goal.Bury || g == Goal.FillGrave || g == Goal.DigGrave || g == Goal.Sleep) && standFacing == Vector2.zero)
            {
                // Graves have no work spot: stop beside it, on the player's side, facing it.
                standSpot = GameApi.GetNearestWalkablePoint(GameApi.GetApproachSpot(pos, 1.0f));
            }
            if (g == Goal.UseDoor && standFacing == Vector2.zero)
            {
                standSpot = GameApi.GetNearestWalkablePoint(standSpot); // the door is in the wall, off the navmesh
            }
            if (!ground && workSpots.TryGetValue(uid, out WorkSpot learned))
            {
                standSpot = learned.Pos;   // where the game put the player last time: no back-and-forth between organs
                standFacing = learned.Facing;
            }

            ModLog.Detail(Lang.T($"Corpos: objetivo {GoalText()}", $"Bodies: goal {GoalText()}"));
            GoTo(Step.Move);
            float dist = GameApi.DistanceTo(standSpot);
            // Until the game reports the real path length, the limit comes from the straight line (stairs and detours make
            // the path much longer: 0.3.32, mine door → house = 29 m straight, walked through forest, village and crossroads).
            moveTimeout = Mathf.Max(settings.MoveTimeoutSeconds.Value, dist / 3.3f * 2f + 10f);
            straightDist = dist;
            pathLengthKnown = false;
            bodyFixes = 0;
            frozenSince = -1f;
            moveStartPending = false;
            ResetProgress();
            // With a known work spot, only skip the walk if already on it: "near" is not enough
            // (standing next to the chest, the game aimed at the chest instead of the table).
            float near = standFacing != Vector2.zero ? 0.5f : NearEnough;
            if (dist <= near)
            {
                GoTo(Step.Aim); // already in place
            }
            else if (GameApi.IsPlayerInWorkState())
            {
                // Interrupted mid-extraction (low energy, sleep): the scalpel animation is still playing. When the game leaves
                // its work state it hands the body back to normal physics, which freezes a path started now (0.3.32).
                moveStartPending = true;
            }
            else if (!GameApi.StartMoveTo(standSpot))
            {
                return FailOrSkipDoor(Lang.T("o jogo recusou o caminho até o alvo", "the game refused the path to the target"));
            }
            return true;
        }

        // ------------------------------------------------------------------ execution

        private TaskResult TickMove()
        {
            if (!TargetStillValid(out string gone))
            {
                GameApi.StopMoving();
                return Replan(gone);
            }
            float dist = GameApi.DistanceTo(standSpot);
            if (moveStartPending)
            {
                if (GameApi.IsPlayerInWorkState() && Now - stepStartedAt < WorkStateWaitMax)
                {
                    Status = Lang.T($"{GoalText()}: esperando terminar o movimento de trabalho", $"{GoalText()}: waiting for the work motion to end");
                    return TaskResult.Running;
                }
                moveStartPending = false;
                pathLengthKnown = false;
                stepStartedAt = Now;
                ResetProgress();
                if (!GameApi.StartMoveTo(standSpot))
                {
                    return FailOrSkipDoorResult(Lang.T("o jogo recusou o caminho até o alvo", "the game refused the path to the target"));
                }
                return TaskResult.Running;
            }
            Status = Lang.T($"{GoalText()}: andando ({dist:0.0} m)", $"{GoalText()}: walking ({dist:0.0} m)");
            MoveState ms = GameApi.GetMoveState();
            if (!pathLengthKnown && GameApi.LastPathLength > 0f)
            {
                pathLengthKnown = true;
                float len = GameApi.LastPathLength;
                moveTimeout = Mathf.Max(settings.MoveTimeoutSeconds.Value, len / 3.3f * 1.5f + 15f);
                ModLog.Detail(Lang.T($"Corpos: caminho de {len:0} m ({straightDist:0} m em linha reta) — limite {moveTimeout:0} s",
                    $"Bodies: path of {len:0} m ({straightDist:0} m in a straight line) — limit {moveTimeout:0} s"));
            }

            if (ms == MoveState.Arrived || dist <= 0.35f)
            {
                GameApi.ClearMoveRequest();
                GoTo(Step.Aim);
                return TaskResult.Running;
            }
            if (ms == MoveState.Failed || ms == MoveState.Idle)
            {
                GameApi.StopMoving();
                if (dist <= NearEnough)
                {
                    GoTo(Step.Aim);
                    return TaskResult.Running;
                }
                if (moveRetries++ < MaxMoveRetries)
                {
                    // Try again; on the 2nd attempt head straight for the object instead of the stop point — only if the
                    // object stands on the player's walkable region (a crematorium's centre is an isolated navmesh island).
                    Vector3 dest = moveRetries == 2 && goal != Goal.UseDoor
                        && GameApi.GetNavArea(targetPos) == GameApi.GetPlayerNavArea() ? targetPos : standSpot;
                    ModLog.Debug(Lang.T($"Movimento falhou; nova tentativa {moveRetries} para {dest}", $"Move failed; retry {moveRetries} to {dest}"));
                    GameApi.StartMoveTo(dest);
                    pathLengthKnown = false;
                    stepStartedAt = Now;
                    ResetProgress();
                    return TaskResult.Running;
                }
                return FailOrSkipDoorResult(Lang.T("não achei caminho até o alvo", "no path found to the target"));
            }
            // Body handed back to normal physics while the path is active: the game will never move the player (it only
            // logs "Trying to move non-static RB by position" every frame). Seen in 0.3.32 after interrupting an extraction.
            if (ms == MoveState.Moving && !GameApi.IsPlayerBodyKinematic())
            {
                if (frozenSince < 0f)
                {
                    frozenSince = Now;
                }
                else if (Now - frozenSince > FrozenBodySeconds)
                {
                    frozenSince = -1f;
                    GameApi.StopMoving();
                    if (bodyFixes++ >= MaxBodyFixes)
                    {
                        return FailOrSkipDoorResult(Lang.T("o jogo não deixa o personagem andar (física normal no meio do caminho)",
                            "the game does not let the character walk (normal physics in the middle of the path)"));
                    }
                    ModLog.Warn(Lang.T($"Corpos: o jogo travou o personagem no caminho (estado: {GameApi.DescribePlayerState()}) — refazendo o caminho ({bodyFixes}/{MaxBodyFixes}).",
                        $"Bodies: the game froze the character on the path (state: {GameApi.DescribePlayerState()}) — restarting the path ({bodyFixes}/{MaxBodyFixes})."));
                    moveStartPending = true;   // starts again at once, or when the work state is over
                    stepStartedAt = Now;
                    return TaskResult.Running;
                }
            }
            else
            {
                frozenSince = -1f;
            }

            // Progress: count only real movement (1 m). Not leaving the spot for StallSeconds = stuck; while it keeps moving
            // the walk goes on (the straight-line limit was too short for long detours).
            Vector3 here = GameApi.GetPlayerPosition();
            if ((here - progressPos).sqrMagnitude >= 1f)
            {
                progressPos = here;
                progressAt = Now;
            }
            else if (Now - progressAt > StallSeconds)
            {
                GameApi.StopMoving();
                if (moveRetries++ < MaxMoveRetries)
                {
                    ModLog.Warn(Lang.T($"Corpos: parado há {StallSeconds:0} s sem sair do lugar a {dist:0} m do alvo (estado: {GameApi.DescribePlayerState()}) — nova tentativa {moveRetries}.",
                        $"Bodies: stuck for {StallSeconds:0} s without moving, {dist:0} m from the target (state: {GameApi.DescribePlayerState()}) — retry {moveRetries}."));
                    moveStartPending = true;
                    stepStartedAt = Now;
                    return TaskResult.Running;
                }
                return FailOrSkipDoorResult(Lang.T("o personagem ficou parado sem sair do lugar", "the character stood still without moving"));
            }
            float walked = Now - stepStartedAt;
            if (walked > moveTimeout)
            {
                // Still moving and the real length is unknown: keep walking up to the hard cap.
                bool moving = Now - progressAt < 3f;
                if (pathLengthKnown || !moving || walked > MaxWalkSeconds)
                {
                    GameApi.StopMoving();
                    return FailOrSkipDoorResult(Lang.T($"demorei demais andando até o alvo ({walked:0} s, faltando {dist:0} m)",
                        $"took too long walking to the target ({walked:0} s, {dist:0} m left)"));
                }
            }
            return TaskResult.Running;
        }

        private void ResetProgress()
        {
            progressPos = GameApi.GetPlayerPosition();
            progressAt = Now;
        }

        private TaskResult TickAim()
        {
            if (!TargetStillValid(out string gone))
            {
                return Replan(gone);
            }

            if (goal == Goal.InspectCrematorium)
            {
                checkedCrem.Add(targetUid);
                ModLog.Detail(Lang.T($"Corpos: crematório checado — estado: {GameApi.GetCraftState(targetUid)}",
                    $"Bodies: crematorium checked — state: {GameApi.GetCraftState(targetUid)}"));
                return Replan(null); // if it is ready, the next plan collects it
            }

            // Goals without a key: autopsy/take body/burial/chest use the UI action directly (near the object).
            if (goal == Goal.DepositChest || goal == Goal.ExtractOrgan || goal == Goal.ExtractPocket || goal == Goal.TakeBody)
            {
                FaceTarget();
                GoTo(Step.Act);
                return TaskResult.Running;
            }

            FaceTarget();
            bool aimed = targetIsGround ? GameApi.IsGroundItemUnderInteraction(targetUid) : GameApi.IsObjectUnderInteraction(targetUid);
            Status = Lang.T($"{GoalText()}: mirando (alvo atual: {GameApi.DescribeInteractionTarget()})",
                $"{GoalText()}: aiming (current target: {GameApi.DescribeInteractionTarget()})");
            if (aimed)
            {
                GameApi.StopMoving();
                if (goal == Goal.FillGrave || goal == Goal.DigGrave)
                {
                    // Digging/filling in the grave is shovel work: hold Action facing it (the shovel must be on the belt).
                    GameApi.SetHoldAction(true);
                    GoTo(Step.Work);
                    return TaskResult.Running;
                }
                if (goal == Goal.CollectCrematorium)
                {
                    GameApi.PressAction(); // the crematorium's "collect all" is on the Action key
                }
                else
                {
                    if (goal == Goal.Sleep)
                    {
                        sleepTries++;
                    }
                    GameApi.PressInteract();
                }
                GoTo(Step.AwaitInteract);
                return TaskResult.Running;
            }
            if (!nudged && Now - stepStartedAt > 1f)
            {
                // Another object got in the way: take a short step toward the target, as the player would.
                nudged = true;
                GameApi.StartMoveTo(Vector3.MoveTowards(GameApi.GetPlayerPosition(), targetPos, 0.4f));
                return TaskResult.Running;
            }
            if (Now - stepStartedAt > AimTimeout)
            {
                GameApi.StopMoving();
                return FailOrSkipDoorResult(Lang.T($"não consegui mirar no alvo (o jogo mira em: {GameApi.DescribeInteractionTarget()})",
                    $"could not aim at the target (the game is aiming at: {GameApi.DescribeInteractionTarget()})"));
            }
            return TaskResult.Running;
        }

        private TaskResult TickAwaitInteract()
        {
            bool done;
            switch (goal)
            {
                case Goal.PickUpBody:
                case Goal.PickUpFromPallet:
                    done = GameApi.IsCarryingBody();
                    if (done && goal == Goal.PickUpFromPallet && parkedPallets.Remove(targetUid))
                    {
                        string c = GameApi.GetCarriedBodyUid();
                        if (c != null)
                        {
                            autopsyDone.Add(c); // parked body: goes straight to the crematorium
                        }
                    }
                    break;
                case Goal.PutOnTable:
                    done = !GameApi.IsCarryingBody() && GameApi.ObjectHasBody(targetUid);
                    break;
                case Goal.ParkOnPallet:
                    done = !GameApi.IsCarryingBody();
                    if (done)
                    {
                        parkedPallets.Add(targetUid);
                    }
                    if (!done && Now - stepStartedAt > InteractTimeout)
                    {
                        failedPallets.Add(targetUid);
                        ModLog.Warn(Lang.T("Corpos: o palete não aceitou o corpo; tentando outro.", "Bodies: the pallet did not accept the body; trying another."));
                        return Replan(null);
                    }
                    break;
                case Goal.Bury:
                    done = !GameApi.IsCarryingBody();
                    if (done)
                    {
                        buriedSpots.Add(targetPos);   // the grave became "grave_body": it still needs filling in
                        ModLog.Info(Lang.T("Corpos: corpo colocado no túmulo — falta fechar com a pá", "Bodies: body placed in the grave — still needs filling in with the shovel"));
                    }
                    break;
                case Goal.Cremate:
                    done = !GameApi.IsCarryingBody();
                    if (done)
                    {
                        bodiesFinished++;
                        ModLog.Info(Lang.T($"Corpos: corpo no crematório ({bodiesFinished} nesta sessão)", $"Bodies: body in the crematorium ({bodiesFinished} this session)"));
                        ResetGoal();
                        Status = Lang.T("corpo cremado", "body cremated");
                        return TaskResult.Succeeded; // one full cycle
                    }
                    break;
                case Goal.CollectCrematorium:
                    done = GameApi.GetCraftState(targetUid) != CraftState.ReadyToCollect;
                    break;
                case Goal.Sleep:
                    // Normally the game takes control away (sleep) and the bot pauses before this; on waking up it replans.
                    done = GameApi.IsSleeping();
                    if (!done && Now - stepStartedAt > SleepStartTimeout)
                    {
                        ModLog.Warn(Lang.T($"Sono: apertei E na cama e o personagem não dormiu (tentativa {sleepTries}/{MaxSleepTries}).",
                            $"Sleep: pressed E on the bed and the character did not sleep (attempt {sleepTries}/{MaxSleepTries})."));
                        return Replan(null);
                    }
                    if (!done)
                    {
                        return TaskResult.Running;
                    }
                    break;
                case Goal.UseDoor:
                    // Normally the game takes control away (fade) and the bot pauses before this; this is the no-fade case.
                    // Doors teleport far away: judge by distance, not by region number (those are renumbered while playing).
                    done = GameApi.DistanceTo(targetPos) > 10f;
                    if (!done && Now - stepStartedAt > DoorTimeout)
                    {
                        return SkipDoor(Lang.T("a porta não levou a lugar nenhum", "the door led nowhere"), DoorBanUseSeconds);
                    }
                    break;
                default:
                    done = true;
                    break;
            }
            if (done)
            {
                if (goal == Goal.CollectCrematorium)
                {
                    QueueCredit();
                }
                ModLog.Info(Lang.T($"Corpos: {GoalText()} — ok", $"Bodies: {GoalText()} — ok"));
                return Replan(null);
            }
            if (goal != Goal.UseDoor && Now - stepStartedAt > InteractTimeout)
            {
                return Fail(Lang.T($"a interação não teve efeito (alvo: {GameApi.DescribeInteractionTarget()})",
                    $"the interaction had no effect (target: {GameApi.DescribeInteractionTarget()})"));
            }
            return TaskResult.Running;
        }

        private TaskResult TickAct()
        {
            string reason;
            switch (goal)
            {
                case Goal.ExtractOrgan:
                    if (partId == null)
                    {
                        // Recipe was already in progress: just work.
                        StartWork();
                        return TaskResult.Running;
                    }
                    if (InventoryFullFor(partId))
                    {
                        return Fail(Lang.T("inventário cheio: libere espaço (ou ligue \"Guardar no baú o que recolheu\") antes de extrair",
                            "inventory full: free up space (or turn on \"Store what the bot collected\") before extracting"));
                    }
                    if (GameApi.StartAutopsyExtract(targetUid, partId, out reason))
                    {
                        ModLog.Detail(Lang.T($"Corpos: extraindo {partId}", $"Bodies: extracting {partId}"));
                        StartWork();
                        return TaskResult.Running;
                    }
                    failedParts.Add(bodyUid + "|" + partId);
                    ModLog.Warn(Lang.T($"Corpos: não deu para extrair {partId}: {reason}. Pulando esse órgão.",
                        $"Bodies: could not extract {partId}: {reason}. Skipping this organ."));
                    return Replan(null);

                case Goal.ExtractPocket:
                    if (InventoryFullFor(partId))
                    {
                        return Fail(Lang.T("inventário cheio: libere espaço (ou ligue \"Guardar no baú o que recolheu\") antes de extrair",
                            "inventory full: free up space (or turn on \"Store what the bot collected\") before extracting"));
                    }
                    if (GameApi.StartPocketExtract(targetUid, partId, out reason))
                    {
                        ModLog.Detail(Lang.T($"Corpos: tirando {partId} (Outros)", $"Bodies: taking out {partId} (Others)"));
                        StartWork();
                        return TaskResult.Running;
                    }
                    failedParts.Add(bodyUid + "|" + partId);
                    ModLog.Warn(Lang.T($"Corpos: não deu para tirar {partId}: {reason}. Pulando esse item.",
                        $"Bodies: could not take out {partId}: {reason}. Skipping this item."));
                    return Replan(null);

                case Goal.DepositChest:
                    return DepositNow();

                case Goal.TakeBody:
                    if (GameApi.TakeBodyFromTable(targetUid, out reason))
                    {
                        if (bodyUid != null)
                        {
                            autopsyDone.Add(bodyUid);
                        }
                        ModLog.Info(Lang.T("Corpos: corpo retirado da mesa", "Bodies: body taken off the table"));
                        return Replan(null);
                    }
                    return Fail(Lang.T("não consegui tirar o corpo da mesa: " + reason, "could not take the body off the table: " + reason));

                default:
                    return Replan(null);
            }
        }

        private TaskResult DepositNow()
        {
            Dictionary<string, int> moved = GameApi.DepositToChest(targetUid, new Dictionary<string, int>(ledger), out string reason);
            if (moved.Count == 0)
            {
                failedChests.Add(targetUid);
                ModLog.Warn(Lang.T($"Baú: nada foi guardado ({reason}). Não uso esse baú de novo até religar o bot.",
                    $"Chest: nothing was stored ({reason}). Not using this chest again until the bot is turned back on."));
                return Replan(null);
            }
            foreach (KeyValuePair<string, int> kv in moved)
            {
                ledger.TryGetValue(kv.Key, out int have);
                int left = have - kv.Value;
                if (left > 0) { ledger[kv.Key] = left; } else { ledger.Remove(kv.Key); }
            }
            freeAfterDeposit = GameApi.PlayerFreeSlots();
            int chestFree = GameApi.ChestFreeSlots(targetUid);
            ModLog.Info(Lang.T("Baú: guardado ", "Chest: stored ") + string.Join(", ", moved.Select(kv => $"{kv.Key} x{kv.Value}"))
                + Lang.T($" (livres agora: {freeAfterDeposit} no inventário", $" (free now: {freeAfterDeposit} in the inventory")
                + (chestFree >= 0 ? Lang.T($", {chestFree} no baú)", $", {chestFree} in the chest)") : ")"));

            // What the bot collected that did not fit (chest out of space/full stack): stays in the inventory and the next chest that accepts it takes it.
            Dictionary<string, int> inInventory = GameApi.SnapshotPlayerItems();
            List<string> notStored = ledger
                .Select(kv => new KeyValuePair<string, int>(kv.Key, Math.Min(kv.Value, inInventory.TryGetValue(kv.Key, out int h) ? h : 0)))
                .Where(kv => kv.Value > 0)
                .Select(kv => $"{kv.Key} x{kv.Value}")
                .ToList();
            if (notStored.Count > 0)
            {
                ModLog.Info(Lang.T($"Baú: não coube em {GameApi.GetObjectDefId(targetUid)}: {string.Join(", ", notStored)}",
                        $"Chest: did not fit in {GameApi.GetObjectDefId(targetUid)}: {string.Join(", ", notStored)}")
                    + (chestFree == 0 ? Lang.T(" — baú cheio", " — chest full") : Lang.T(" — o baú recusou (pilha cheia ou filtro)", " — the chest refused (full stack or filter)"))
                    + Lang.T("; ficam no inventário até um baú aceitar.", "; they stay in the inventory until a chest accepts them."));
            }
            return Replan(null);
        }

        /// <summary>Closes the current collection window; whatever reaches the inventory in the next few seconds counts as collected by the bot.</summary>
        private void QueueCredit()
        {
            if (invBefore == null)
            {
                return;
            }
            bool extraction = goal == Goal.ExtractOrgan || goal == Goal.ExtractPocket;
            pendingCredits.Add(new PendingCredit
            {
                Before = invBefore,
                ItemId = extraction ? workItemId : null,
                Cap = extraction ? 1 : 0,
                Due = Now + CreditDelay,
            });
            invBefore = null;
        }

        /// <summary>Adds the due pending credits to the ledger (what the bot collected): inventory gain since the snapshot.</summary>
        private void SettleCredits()
        {
            if (pendingCredits.Count == 0)
            {
                return;
            }
            Dictionary<string, int> now = null;
            for (int i = 0; i < pendingCredits.Count; i++)
            {
                PendingCredit p = pendingCredits[i];
                if (Now < p.Due)
                {
                    continue;
                }
                pendingCredits.RemoveAt(i--);
                if (Now - p.Due > CreditMaxAge)
                {
                    continue; // the bot was stopped: what entered the inventory may be the player's
                }
                if (now == null)
                {
                    now = GameApi.SnapshotPlayerItems();
                }
                int budget = p.Cap > 0 ? p.Cap : int.MaxValue;
                foreach (KeyValuePair<string, int> kv in now)
                {
                    if (p.ItemId != null ? kv.Key != p.ItemId : ExpectedElsewhere(kv.Key))
                    {
                        continue;
                    }
                    p.Before.TryGetValue(kv.Key, out int before);
                    int gained = Math.Min(kv.Value - before, budget);
                    if (gained <= 0)
                    {
                        continue;
                    }
                    ledger.TryGetValue(kv.Key, out int have);
                    ledger[kv.Key] = have + gained;
                    budget -= gained;
                    ModLog.Debug(Lang.T($"Registro: +{gained} {kv.Key} (recolhido pelo bot; total {ledger[kv.Key]})",
                        $"Ledger: +{gained} {kv.Key} (collected by the bot; total {ledger[kv.Key]})"));
                    if (budget <= 0)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>Item expected by another collection (pending or in-progress extraction): does not count in the "any item" window.</summary>
        private bool ExpectedElsewhere(string itemId)
            => itemId == workItemId || pendingCredits.Any(p => p.ItemId == itemId);

        private void StartWork()
        {
            FaceTarget();
            lastProgress = GameApi.GetCraftProgressTicks(targetUid);
            lastProgressAt = Now;
            misaimSince = -1f;
            GoTo(Step.Work);
            if (AimedElsewhere(out _))
            {
                BeginMisaim(); // don't hold Action: fix the aim first
                return;
            }
            GameApi.SetHoldAction(true);
        }

        /// <summary>No free slot and no stack of the same item to add to: the extraction would have nowhere to land.</summary>
        private static bool InventoryFullFor(string itemId)
            => GameApi.PlayerFreeSlots() == 0 && !GameApi.SnapshotPlayerItems().ContainsKey(itemId);

        /// <summary>Is the game aiming at ANOTHER object (not the target)? No target at all does not count.</summary>
        private bool AimedElsewhere(out string what)
        {
            string cur = GameApi.GetInteractionTargetUid();
            what = cur == null ? null : GameApi.DescribeInteractionTarget();
            return cur != null && cur != targetUid;
        }

        private void BeginMisaim()
        {
            GameApi.SetHoldAction(false);
            if (misaimSince < 0f)
            {
                misaimSince = Now;
                ModLog.Warn(Lang.T($"Corpos: o jogo está mirando em {GameApi.DescribeInteractionTarget()}, não no alvo — soltei a Ação e vou reposicionar.",
                    $"Bodies: the game is aiming at {GameApi.DescribeInteractionTarget()}, not the target — released Action, repositioning."));
                GameApi.StartMoveTo(standSpot);
            }
            FaceTarget();
        }

        /// <summary>
        /// Work safety guard: if the aim leaves the target, release Action at once (holding Action on a chest
        /// takes everything from it into the inventory). Returns a result when the step should stop here.
        /// </summary>
        private TaskResult? GuardAim()
        {
            if (AimedElsewhere(out string what))
            {
                BeginMisaim();
                lastProgressAt = Now; // does not count as stalled work while fixing the aim
                if (Now - misaimSince > MisaimTimeout)
                {
                    return Fail(Lang.T($"o jogo continua mirando em {what}, não no alvo (soltei a Ação para não mexer nele)",
                        $"the game keeps aiming at {what}, not the target (released Action so it is not touched)"));
                }
                return TaskResult.Running;
            }
            if (misaimSince >= 0f)
            {
                misaimSince = -1f;
                GameApi.StopMoving();
                FaceTarget();
                GameApi.SetHoldAction(true);
                lastProgressAt = Now;
            }
            return null;
        }

        private TaskResult TickFill()
        {
            // Check the id first: when the grave changes object, the game's aim moves to the new object (not a "wrong aim").
            string id = GameApi.GetObjectDefId(targetUid);
            if (id != "grave_body")   // became grave_ground (or the object was replaced): grave filled in
            {
                GameApi.SetHoldAction(false);
                buriedSpots.RemoveAll(sp => Vector3.Distance(sp, targetPos) < SameSpot);
                bodiesFinished++;
                ModLog.Info(Lang.T($"Corpos: corpo enterrado ({bodiesFinished} nesta sessão)", $"Bodies: body buried ({bodiesFinished} this session)"));
                ResetGoal();
                Status = Lang.T("corpo enterrado", "body buried");
                return TaskResult.Succeeded; // one full cycle
            }
            TaskResult? guard = GuardAim();
            if (guard.HasValue)
            {
                return guard.Value;
            }
            Status = Lang.T($"{GoalText()}: trabalhando ({Now - stepStartedAt:0}s)", $"{GoalText()}: working ({Now - stepStartedAt:0}s)");
            if (Now - stepStartedAt > FillTimeout)
            {
                GameApi.SetHoldAction(false);
                return Fail(Lang.T($"o túmulo não fechou em {FillTimeout:0}s (pá no cinto? energia? alvo do jogo: {GameApi.DescribeInteractionTarget()})",
                    $"the grave was not filled in within {FillTimeout:0}s (shovel on the belt? energy? game target: {GameApi.DescribeInteractionTarget()})"));
            }
            return TaskResult.Running;
        }

        private TaskResult TickDig()
        {
            // Check the id first: when the grave changes object, the game's aim moves to the new object (not a "wrong aim").
            string id = GameApi.GetObjectDefId(targetUid);
            if (id != "grave_empty_place")   // became grave_empty (or the object was replaced): grave open
            {
                GameApi.SetHoldAction(false);
                ModLog.Info(Lang.T("Corpos: túmulo cavado — pronto para o corpo", "Bodies: grave dug — ready for the body"));
                return Replan(null);
            }
            TaskResult? guard = GuardAim();
            if (guard.HasValue)
            {
                return guard.Value;
            }
            Status = Lang.T($"{GoalText()}: trabalhando ({Now - stepStartedAt:0}s)", $"{GoalText()}: working ({Now - stepStartedAt:0}s)");
            if (Now - stepStartedAt > FillTimeout)
            {
                GameApi.SetHoldAction(false);
                return Fail(Lang.T($"o túmulo não ficou pronto em {FillTimeout:0}s (pá no cinto? energia? alvo do jogo: {GameApi.DescribeInteractionTarget()})",
                    $"the grave was not ready within {FillTimeout:0}s (shovel on the belt? energy? game target: {GameApi.DescribeInteractionTarget()})"));
            }
            return TaskResult.Running;
        }

        private TaskResult TickWork()
        {
            if (goal == Goal.FillGrave)
            {
                return TickFill();
            }
            if (goal == Goal.DigGrave)
            {
                return TickDig();
            }
            // Done when the recipe left the queue (or the grave turned into something else).
            bool finished = !GameApi.IsCraftActive(targetUid);
            if (!finished)
            {
                TaskResult? guard = GuardAim();
                if (guard.HasValue)
                {
                    return guard.Value;
                }
            }
            if (finished)
            {
                GameApi.SetHoldAction(false);
                ModLog.Info(Lang.T($"Corpos: {GoalText()} concluído", $"Bodies: {GoalText()} done"));
                QueueCredit();
                return Replan(null);
            }

            int progress = GameApi.GetCraftProgressTicks(targetUid);
            if (progress != lastProgress)
            {
                lastProgress = progress;
                lastProgressAt = Now;
                LearnWorkSpot();
            }
            Status = Lang.T($"{GoalText()}: trabalhando (progresso {Math.Max(progress, 0)})", $"{GoalText()}: working (progress {Math.Max(progress, 0)})");
            if (Now - lastProgressAt > settings.WorkStallSeconds.Value)
            {
                GameApi.SetHoldAction(false);
                return Fail(Lang.T($"o trabalho não avança há {settings.WorkStallSeconds.Value:0}s (ferramenta no cinto? energia? alvo do jogo: {GameApi.DescribeInteractionTarget()})",
                    $"no work progress for {settings.WorkStallSeconds.Value:0}s (tool on the belt? energy? game target: {GameApi.DescribeInteractionTarget()})"));
            }
            return TaskResult.Running;
        }

        // ------------------------------------------------------------------ utilities

        /// <summary>Work progressed: the player is on the work spot the game chose. Remember it for the next goals on the same object.</summary>
        private void LearnWorkSpot()
        {
            if (targetIsGround || targetUid == null)
            {
                return;
            }
            Vector3 pos = GameApi.GetPlayerPosition();
            Vector2 facing = GameApi.GetPlayerFacing();
            if (pos == Vector3.zero)
            {
                return;
            }
            bool known = workSpots.TryGetValue(targetUid, out WorkSpot old);
            if (known && Vector3.Distance(old.Pos, pos) < 0.05f)
            {
                return;
            }
            workSpots[targetUid] = new WorkSpot { Pos = pos, Facing = facing.sqrMagnitude > 0.01f ? facing.normalized : standFacing };
            if (Vector3.Distance(pos, standSpot) > 0.3f)
            {
                ModLog.Detail(Lang.T(
                    $"Corpos: o jogo trabalha em {GameApi.GetObjectDefId(targetUid)} a partir de {pos.x:0.00},{pos.z:0.00} "
                    + $"(o bot tinha ido a {standSpot.x:0.00},{standSpot.z:0.00}) — uso o ponto do jogo daqui em diante",
                    $"Bodies: the game works on {GameApi.GetObjectDefId(targetUid)} from {pos.x:0.00},{pos.z:0.00} "
                    + $"(the bot had gone to {standSpot.x:0.00},{standSpot.z:0.00}) — using the game's spot from now on"));
            }
            standSpot = pos;
            standFacing = workSpots[targetUid].Facing;
        }

        private void FaceTarget()
        {
            if (!targetIsGround && standFacing.sqrMagnitude > 0.01f && GameApi.DistanceTo(standSpot) < 0.5f)
            {
                GameApi.FaceDirection(standFacing);
            }
            else
            {
                GameApi.FaceTowards(targetPos);
            }
        }

        private bool TargetStillValid(out string reason)
        {
            reason = null;
            if (targetIsGround)
            {
                if (!GameApi.GroundItemExists(targetUid))
                {
                    reason = Lang.T("o corpo não está mais no chão", "the body is no longer on the ground");
                }
            }
            else if (!GameApi.ObjectExists(targetUid))
            {
                reason = Lang.T("o objeto alvo sumiu", "the target object is gone");
            }
            return reason == null;
        }

        /// <summary>
        /// Door that did not work: leave it out of the routes for a while and try another route, instead of turning the bot
        /// off. Never for the whole session — 0.3.32 banned, one walk at a time, the only door into the house.
        /// </summary>
        private TaskResult SkipDoor(string why, float seconds = DoorBanWalkSeconds)
        {
            ModLog.Warn(Lang.T($"Corpos: porta {WorldObjectRef.ShortUid(targetUid)} ignorada por {seconds:0} s — {why}",
                $"Bodies: door {WorldObjectRef.ShortUid(targetUid)} ignored for {seconds:0} s — {why}"));
            nav.MarkDoorBroken(targetUid, seconds);
            return Replan(null);
        }

        private TaskResult FailOrSkipDoorResult(string why) => goal == Goal.UseDoor ? SkipDoor(why) : Fail(why);

        private bool FailOrSkipDoor(string why) => FailOrSkipDoorResult(why) != TaskResult.Failed;

        private TaskResult Replan(string why)
        {
            if (why != null)
            {
                ModLog.Info(Lang.T($"Corpos: replanejando ({why})", $"Bodies: replanning ({why})"));
            }
            GameApi.SetHoldAction(false);
            ResetGoal();
            return TaskResult.Running;
        }

        private TaskResult Fail(string why)
        {
            planFailed = true;
            GameApi.ReleaseAllVirtualKeys();
            ModLog.Warn(Lang.T($"Corpos: {GoalText()} falhou — {why}", $"Bodies: {GoalText()} failed — {why}"));
            ResetGoal();
            Status = why;
            return TaskResult.Failed;
        }

        private void ResetGoal()
        {
            goal = Goal.None;
            step = Step.Plan;
            targetUid = null;
            partId = null;
            bodyUid = null;
        }

        private void GoTo(Step s)
        {
            step = s;
            stepStartedAt = Now;
            nudged = false;
        }

        private string GoalText() => goal == Goal.UseDoor
            ? Lang.T($"atravessar porta ({travelDoors} no caminho) para {travelPurpose}", $"go through door ({travelDoors} on the path) to {travelPurpose}")
            : GoalText(goal, partId);

        private static string GoalText(Goal g, string part)
        {
            switch (g)
            {
                case Goal.PickUpBody: return Lang.T("pegar corpo do chão", "pick up body from the ground");
                case Goal.PutOnTable: return Lang.T("colocar corpo na mesa", "put body on the table");
                case Goal.ExtractOrgan: return part != null ? Lang.T($"extrair {part}", $"extract {part}") : Lang.T("continuar autópsia", "continue autopsy");
                case Goal.ExtractPocket: return Lang.T($"tirar {part}", $"take out {part}");
                case Goal.TakeBody: return Lang.T("tirar corpo da mesa", "take body off the table");
                case Goal.Bury: return Lang.T("enterrar corpo", "bury body");
                case Goal.PickUpFromPallet: return Lang.T("pegar corpo do palete", "pick up body from the pallet");
                case Goal.Cremate: return Lang.T("levar corpo ao crematório", "take body to the crematorium");
                case Goal.CollectCrematorium: return Lang.T("recolher o crematório", "collect the crematorium");
                case Goal.UseDoor: return Lang.T("atravessar porta", "go through door");
                case Goal.InspectCrematorium: return Lang.T("checar o crematório", "check the crematorium");
                case Goal.FillGrave: return Lang.T("fechar o túmulo", "fill in the grave");
                case Goal.DigGrave: return Lang.T("cavar o túmulo marcado", "dig the marked grave");
                case Goal.Sleep: return Lang.T("dormir na cama de casa", "sleep in the home bed");
                case Goal.ParkOnPallet: return Lang.T("deixar corpo no palete (crematório ocupado)", "leave body on the pallet (crematorium busy)");
                case Goal.DepositChest: return Lang.T("guardar itens no baú", "store items in the chest");
                default: return "-";
            }
        }
    }
}

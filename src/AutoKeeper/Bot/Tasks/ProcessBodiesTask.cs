using System;
using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot.Tasks
{
    /// <summary>
    /// Rotina "processar corpos" (layout real do necrotério, dump do jogo 1.007):
    ///   palete (pallet_corpse) ou chão → mesa de autópsia livre → extrair órgãos e "Outros" (config) → tirar corpo →
    ///   destino: crematório (padrão), deixar na mesa, ou cova vazia (experimental).
    /// Cada Tick decide UM objetivo a partir do estado do mundo (replaneja sozinho depois de pausa/abort)
    /// e o executa em passos curtos: andar → mirar → apertar E / iniciar receita → segurar Ação até terminar.
    /// Se o objetivo está em outra área (atrás de portas), o objetivo vira "usar a próxima porta" do caminho.
    /// Usa só a GameApi; nada de teleporte do mod, spawn ou edição de save.
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

        /// <summary>Objeto candidato com a região e o custo (m) para chegar nele a partir do jogador.</summary>
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

        /// <summary>Foto do mundo usada numa decisão (só dados copiados da GameApi).</summary>
        private sealed class WorldView
        {
            public uint Here;
            public Dictionary<uint, AreaRoute> Routes;
            public List<Candidate> Tables;
            public List<Candidate> Pallets;
            public List<Candidate> Crematoriums;
            public List<Candidate> Graves;
            public List<GroundItemRef> GroundBodies;
            public List<Candidate> Chests;
            public List<Candidate> GraveBodies;
        }

        /// <summary>O que fazer agora (sem efeitos colaterais; PlanNext aplica).</summary>
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
            public string Text;   // motivo (Wait/Fail)
        }

        private const float NearEnough = 2.2f;        // distância do ponto de parada considerada "cheguei"
        private const float AimTimeout = 2.5f;
        private const float InteractTimeout = 3f;
        private const float DoorTimeout = 4f;
        private const int MaxMoveRetries = 2;
        private const float SameRoomDistance = 20f;  // objeto sem região conhecida mas perto = mesma sala
        private const float IdleRecheckSeconds = 2f;

        private readonly Settings settings;
        private readonly Navigator nav;

        // Memória entre objetivos (não depende de tipos do jogo).
        private readonly HashSet<string> autopsyDone = new HashSet<string>();        // corpos que já passaram pela mesa
        private readonly HashSet<string> failedParts = new HashSet<string>();         // "corpo|item" que o jogo recusou
        private readonly HashSet<string> masterySkipLogged = new HashSet<string>();   // "corpo|item" pulado por maestria (log 1x)
        private int bodiesFinished;

        // Crematório: checar ao chegar; baú: só o que o bot recolheu (extração/crematório).
        private readonly HashSet<string> checkedCrem = new HashSet<string>();
        private readonly HashSet<string> failedChests = new HashSet<string>();
        private readonly Dictionary<string, int> ledger = new Dictionary<string, int>();   // itemId → unidades recolhidas pelo bot
        private Dictionary<string, int> invBefore;                                          // foto do inventário antes de extrair/recolher
        private uint prevHere;
        private readonly List<Vector3> buriedSpots = new List<Vector3>();   // covas onde o bot colocou corpo e que ainda precisam ser fechadas
        private const float FillTimeout = 90f;
        private const float SameSpot = 1.6f;
        private int seenGeneration = -1;
        private bool chestWarned;

        // Objetivo atual.
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
        private int lastProgress;
        private float lastProgressAt;
        private bool planFailed;
        private bool nudged;
        private uint doorFromArea;
        private string travelPurpose;   // para o painel: o que o bot vai fazer do outro lado
        private int travelDoors;

        // Ociosidade: não refazer o planejamento completo a cada tick.
        private float idleCheckedAt = -999f;
        private string idleReason;

        public ProcessBodiesTask(Settings settings, Navigator nav)
        {
            this.settings = settings;
            this.nav = nav;
        }

        public string Name => "Processar corpos";

        public string Status { get; private set; } = "aguardando";

        private static float Now => Time.unscaledTime;

        private float Radius => settings.SearchRadius.Value;

        // ------------------------------------------------------------------ ITask

        public bool CanRun(out string reason)
        {
            if (!settings.BodiesEnabled.Value)
            {
                reason = "desativada na config";
                return false;
            }
            if (GameApi.IsCarryingBody() && !GameApi.CarriedBodyIsPlain())
            {
                reason = "carregando corpo de zumbi/demônio (fora do MVP)";
                return false;
            }
            if (!GameApi.IsCarryingBody() && GameApi.IsCarryingAnything())
            {
                reason = "mãos ocupadas com outro item";
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
                ? "sem corpos para processar (nem atrás das portas)"
                : "sem corpos para processar nesta área (\"Ir sozinho até o trabalho\" está desligado)";
            reason = idleReason;
            return false;
        }

        public TaskResult Tick()
        {
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
                        Status = bodiesFinished > 0 ? $"nada mais a fazer ({bodiesFinished} corpo(s) concluído(s))" : "nada a fazer";
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
                    return Fail("passo desconhecido");
            }
        }

        public void Abort()
        {
            GameApi.ReleaseAllVirtualKeys();
            GameApi.StopMoving();
            ResetGoal();
            Status = "interrompido";
        }

        // ------------------------------------------------------------------ foto do mundo

        private WorldView Look()
        {
            var w = new WorldView
            {
                Here = GameApi.GetPlayerNavArea(),
            };
            if (seenGeneration != nav.Generation)
            {
                seenGeneration = nav.Generation;
                checkedCrem.Clear();   // bot religado: checa o crematório de novo
                failedChests.Clear();
                chestWarned = false;
            }
            if (w.Here != 0 && w.Here != prevHere)
            {
                if (prevHere != 0)
                {
                    checkedCrem.Clear();   // chegou noutra área: checa o crematório de novo
                }
                prevHere = w.Here;
            }
            w.Routes = w.Here != 0 ? nav.ReachableAreas(settings.TravelEnabled.Value) : new Dictionary<uint, AreaRoute>();
            w.Tables = Collect(ObjectKind.AutopsyTable, w);
            w.Pallets = Collect(ObjectKind.MorguePallet, w);
            w.Crematoriums = Collect(ObjectKind.Crematorium, w);
            w.Graves = settings.Destination.Value == BodyDestination.Grave ? Collect(ObjectKind.EmptyGrave, w) : new List<Candidate>();
            bool wantsChest = settings.UseChest.Value && LedgerTotal() > 0 && GameApi.PlayerFreeSlots() < settings.ChestFreeSlots.Value;
            w.Chests = wantsChest ? Collect(ObjectKind.Chest, w) : new List<Candidate>();
            w.GraveBodies = settings.Destination.Value == BodyDestination.Grave && buriedSpots.Count > 0
                ? Collect(ObjectKind.GraveBody, w).Where(c => buriedSpots.Any(sp => Vector3.Distance(sp, c.Obj.Position) < SameSpot)).ToList()
                : new List<Candidate>();
            w.GroundBodies = GameApi.FindGroundBodies(Radius)
                .Where(b => w.Here == 0 || GameApi.GetNavArea(b.Position) == w.Here)
                .ToList();
            return w;
        }

        /// <summary>Objetos do tipo alcançáveis (nesta área ou atrás de portas), do mais barato ao mais caro.</summary>
        private List<Candidate> Collect(ObjectKind kind, WorldView w)
        {
            var list = new List<Candidate>();
            foreach (WorldObjectRef o in GameApi.FindObjects(kind, float.MaxValue))
            {
                float straight = GameApi.DistanceTo(o.Position);
                uint area = w.Here == 0 ? 0 : nav.AreaOf(o.Uid, o.Position);
                if (area != 0 && w.Routes.TryGetValue(area, out AreaRoute route))
                {
                    list.Add(new Candidate(o, area, area == w.Here ? straight : Navigator.CostTo(route, o.Position)));
                }
                else if (straight <= SameRoomDistance || (w.Here == 0 && straight <= Radius))
                {
                    // Região desconhecida mas perto: mesma sala (comportamento da 0.2.x).
                    list.Add(new Candidate(o, w.Here, straight));
                }
            }
            list.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            return list;
        }

        // ------------------------------------------------------------------ decisão

        /// <summary>Escolhe o próximo objetivo a partir do estado do mundo. Em dryRun só responde se há o que fazer.</summary>
        private bool PlanNext(bool dryRun)
        {
            WorldView w = Look();
            Decision d = Decide(w);
            switch (d.Kind)
            {
                case DecisionKind.None:
                    return false;
                case DecisionKind.Fail:
                    return dryRun || Fail(d.Text) != TaskResult.Failed;
                case DecisionKind.Wait:
                    if (IsRemote(w, d.Area))
                    {
                        return BeginTravel(dryRun, w, d, "esperar: " + d.Text);
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

        /// <summary>Regras de "antes de tudo" (checar crematório, guardar no baú) por cima do plano normal.</summary>
        private Decision Decide(WorldView w)
        {
            Decision d = DecideCore(w);
            if (d.Kind == DecisionKind.None || d.Kind == DecisionKind.Fail || GameApi.IsCarryingBody())
            {
                return d;
            }

            // Ao chegar no necrotério: passa no crematório antes de começar (recolhe o que estiver pronto).
            if (settings.CheckCrematoriumFirst.Value && settings.Destination.Value == BodyDestination.Crematorium
                && d.Goal != Goal.CollectCrematorium)
            {
                // O estado é lido à distância: só vai até lá se há algo pronto para recolher (nada de visita a crematório vazio).
                Candidate? crem = FirstFree(w.Crematoriums, c => !GameApi.HasOtherWorker(c.Obj.Uid)
                    && GameApi.GetCraftState(c.Obj.Uid) == CraftState.ReadyToCollect);
                if (crem.HasValue)
                {
                    return Act(Goal.CollectCrematorium, crem.Value, null);
                }
            }

            // Inventário quase cheio: leva ao baú só o que o bot recolheu.
            if (settings.UseChest.Value && LedgerTotal() > 0 && GameApi.PlayerFreeSlots() < settings.ChestFreeSlots.Value)
            {
                // Prefere o baú que já guarda esses itens (o "baú de guardar" do necrotério); senão o mais perto que aceite.
                Func<Candidate, bool> usable = c => !failedChests.Contains(c.Obj.Uid) && GameApi.ChestCanTakeAny(c.Obj.Uid, ledger.Keys);
                Candidate? chest = FirstFree(w.Chests, c => usable(c) && GameApi.ChestHasAny(c.Obj.Uid, ledger.Keys)) ?? FirstFree(w.Chests, usable);
                if (chest.HasValue)
                {
                    return Act(Goal.DepositChest, chest.Value, null);
                }
                if (!chestWarned)
                {
                    chestWarned = true;
                    ModLog.Warn("Baú: inventário quase cheio, mas não achei baú alcançável que aceite os itens do bot.");
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
            List<Candidate> tables = w.Tables.Where(t => !GameApi.HasOtherWorker(t.Obj.Uid)).ToList(); // zumbi/NPC trabalhando: não mexer
            Candidate? crem = dest == BodyDestination.Crematorium ? FirstFree(w.Crematoriums, c => !GameApi.HasOtherWorker(c.Obj.Uid)) : null;
            CraftState cremState = crem.HasValue ? GameApi.GetCraftState(crem.Value.Obj.Uid) : CraftState.Unknown;
            bool cremFree = crem.HasValue && cremState == CraftState.Idle && !GameApi.ObjectHasBody(crem.Value.Obj.Uid);
            Candidate? grave = dest == BodyDestination.Grave
                ? FirstFree(w.Graves, g => !GameApi.ObjectHasBody(g.Obj.Uid) && !GameApi.IsCraftActive(g.Obj.Uid) && !GameApi.HasOtherWorker(g.Obj.Uid))
                : null;
            Candidate? freeTable = FirstFree(tables, t => !GameApi.ObjectHasBody(t.Obj.Uid) && !GameApi.IsCraftActive(t.Obj.Uid));

            // 1) Carregando um corpo: mesa (se ainda não fez autópsia) ou destino.
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
                            return FailWith("carregando corpo, mas não achei crematório alcançável");
                        }
                        if (cremFree)
                        {
                            return Act(Goal.Cremate, crem.Value, carried);
                        }
                        return WaitAt(crem.Value.Area, "aguardando o crematório ficar livre");
                    case BodyDestination.Grave:
                        if (grave.HasValue)
                        {
                            return Act(Goal.Bury, grave.Value, carried);
                        }
                        return FailWith("carregando corpo, mas não há cova vazia (grave_empty) livre alcançável — abra/construa uma cova no cemitério");
                    default:
                        if (freeTable.HasValue)
                        {
                            return Act(Goal.PutOnTable, freeTable.Value, carried);
                        }
                        return FailWith("carregando corpo, mas não há mesa de autópsia livre");
                }
            }

            // 1b) Corpo já colocado numa cova: fechar a cova com a pá (trabalho de "grave_body").
            if (w.GraveBodies.Count > 0)
            {
                return Act(Goal.FillGrave, w.GraveBodies[0], null);
            }

            // 2) Crematório terminou: recolher o resultado antes de qualquer coisa (libera para o próximo corpo).
            if (crem.HasValue && cremState == CraftState.ReadyToCollect)
            {
                return Act(Goal.CollectCrematorium, crem.Value, null);
            }

            // 3) Mesas com corpo comum.
            foreach (Candidate t in tables)
            {
                string uid = t.Obj.Uid;
                if (!GameApi.ObjectHasPlainBody(uid))
                {
                    continue;
                }
                string body = GameApi.GetBodyUidInObject(uid);

                // 3a) Receita em andamento (extração): trabalhar nela.
                if (GameApi.IsCraftActive(uid))
                {
                    return Act(Goal.ExtractOrgan, t, body);
                }

                // 3b) Ainda há órgão / item de "Outros" desejado para extrair.
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

                // 3c) Autópsia concluída: tirar da mesa só se o destino estiver disponível agora.
                bool destReady = (dest == BodyDestination.Crematorium && cremFree) || (dest == BodyDestination.Grave && grave.HasValue);
                if (destReady)
                {
                    return Act(Goal.TakeBody, t, body);
                }
            }

            // 4) Buscar um corpo novo (palete primeiro, depois chão) se houver mesa livre
            //    ou, sem autópsia, se o destino estiver disponível.
            bool canReceive = wantsParts
                ? freeTable.HasValue
                : (dest == BodyDestination.Crematorium && cremFree) || (dest == BodyDestination.Grave && grave.HasValue);
            if (!canReceive)
            {
                return default;
            }
            foreach (Candidate pallet in w.Pallets)
            {
                if (GameApi.ObjectHasPlainBody(pallet.Obj.Uid))
                {
                    return Act(Goal.PickUpFromPallet, pallet, null);
                }
            }
            if (w.GroundBodies.Count > 0)
            {
                GroundItemRef b = w.GroundBodies[0];
                return new Decision { Kind = DecisionKind.Act, Goal = Goal.PickUpBody, Uid = b.Uid, Pos = b.Position, Area = w.Here, Ground = true };
            }
            return default;
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

        /// <summary>Próximo item a extrair: pula os que o jogo recusou e, com "Só extrair com maestria", os de chance baixa.</summary>
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
                            ModLog.Info($"Corpos: pulando {id} — {m}, abaixo do mínimo de {settings.MinMasteryChance.Value}%");
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

        /// <summary>Nada a fazer agora, mas a tarefa continua ativa (ex.: esperando o crematório).</summary>
        private bool Wait(bool dryRun, string why)
        {
            if (!dryRun)
            {
                if (Status != why)
                {
                    ModLog.Info("Corpos: " + why);
                }
                Status = why;
            }
            return true;
        }

        // ------------------------------------------------------------------ início de objetivo

        /// <summary>O alvo fica em outra área: o objetivo agora é usar a primeira porta do caminho.</summary>
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
            doorFromArea = w.Here;
            ModLog.Info($"Corpos: \"{purpose}\" fica em outra área — indo pela porta \"{door.Label}\" ({route.Doors} porta(s) no caminho, ~{route.Cost:0} m)");
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

            if (ground)
            {
                standSpot = GameApi.GetApproachSpot(pos, 0.8f);
                standFacing = Vector2.zero;
            }
            else if (!GameApi.TryGetStandSpot(uid, out standSpot, out standFacing))
            {
                return Fail($"alvo {WorldObjectRef.ShortUid(uid)} sumiu") != TaskResult.Failed;
            }
            if ((g == Goal.Bury || g == Goal.FillGrave) && standFacing == Vector2.zero)
            {
                // Covas não têm ponto de trabalho: para ao lado, vindo do lado do jogador, de frente para ela.
                standSpot = GameApi.GetNearestWalkablePoint(GameApi.GetApproachSpot(pos, 1.0f));
            }
            if (g == Goal.UseDoor && standFacing == Vector2.zero)
            {
                standSpot = GameApi.GetNearestWalkablePoint(standSpot); // a porta fica na parede, fora do navmesh
            }

            ModLog.Info($"Corpos: objetivo {GoalText()}");
            GoTo(Step.Move);
            float dist = GameApi.DistanceTo(standSpot);
            moveTimeout = Mathf.Max(settings.MoveTimeoutSeconds.Value, dist / 3.3f * 2f + 10f);
            if (dist <= NearEnough)
            {
                GoTo(Step.Aim); // já está perto
            }
            else if (!GameApi.StartMoveTo(standSpot))
            {
                return FailOrSkipDoor("o jogo recusou o caminho até o alvo");
            }
            return true;
        }

        // ------------------------------------------------------------------ execução

        private TaskResult TickMove()
        {
            if (!TargetStillValid(out string gone))
            {
                GameApi.StopMoving();
                return Replan(gone);
            }
            float dist = GameApi.DistanceTo(standSpot);
            Status = $"{GoalText()}: andando ({dist:0.0} m)";
            MoveState ms = GameApi.GetMoveState();

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
                    // Tenta de novo; na 2ª vez mira direto no objeto em vez do ponto de parada.
                    Vector3 dest = moveRetries == 2 && goal != Goal.UseDoor ? targetPos : standSpot;
                    ModLog.Debug($"Movimento falhou; nova tentativa {moveRetries} para {dest}");
                    GameApi.StartMoveTo(dest);
                    stepStartedAt = Now;
                    return TaskResult.Running;
                }
                return FailOrSkipDoorResult("não achei caminho até o alvo");
            }
            if (Now - stepStartedAt > moveTimeout)
            {
                GameApi.StopMoving();
                return FailOrSkipDoorResult("demorei demais andando até o alvo");
            }
            return TaskResult.Running;
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
                ModLog.Info($"Corpos: crematório checado — estado: {GameApi.GetCraftState(targetUid)}");
                return Replan(null); // se estiver pronto, o próximo plano recolhe
            }

            // Objetivos sem tecla: autópsia/tirar corpo/enterro/baú usam a ação de UI direto (perto do objeto).
            if (goal == Goal.DepositChest || goal == Goal.ExtractOrgan || goal == Goal.ExtractPocket || goal == Goal.TakeBody)
            {
                FaceTarget();
                GoTo(Step.Act);
                return TaskResult.Running;
            }

            FaceTarget();
            bool aimed = targetIsGround ? GameApi.IsGroundItemUnderInteraction(targetUid) : GameApi.IsObjectUnderInteraction(targetUid);
            Status = $"{GoalText()}: mirando (alvo atual: {GameApi.DescribeInteractionTarget()})";
            if (aimed)
            {
                GameApi.StopMoving();
                if (goal == Goal.FillGrave)
                {
                    // Fechar a cova é trabalho com pá: segurar Ação de frente para ela (a pá precisa estar no cinto).
                    GameApi.SetHoldAction(true);
                    GoTo(Step.Work);
                    return TaskResult.Running;
                }
                if (goal == Goal.CollectCrematorium)
                {
                    GameApi.PressAction(); // "recolher tudo" do crematório é na tecla de ação
                }
                else
                {
                    GameApi.PressInteract();
                }
                GoTo(Step.AwaitInteract);
                return TaskResult.Running;
            }
            if (!nudged && Now - stepStartedAt > 1f)
            {
                // Outro objeto ficou na frente: dá um passo curto na direção do alvo, como o jogador faria.
                nudged = true;
                GameApi.StartMoveTo(Vector3.MoveTowards(GameApi.GetPlayerPosition(), targetPos, 0.4f));
                return TaskResult.Running;
            }
            if (Now - stepStartedAt > AimTimeout)
            {
                GameApi.StopMoving();
                return FailOrSkipDoorResult($"não consegui mirar no alvo (o jogo mira em: {GameApi.DescribeInteractionTarget()})");
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
                    break;
                case Goal.PutOnTable:
                    done = !GameApi.IsCarryingBody() && GameApi.ObjectHasBody(targetUid);
                    break;
                case Goal.Bury:
                    done = !GameApi.IsCarryingBody();
                    if (done)
                    {
                        buriedSpots.Add(targetPos);   // a cova virou "grave_body": falta fechá-la
                        ModLog.Info("Corpos: corpo colocado na cova — falta fechar com a pá");
                    }
                    break;
                case Goal.Cremate:
                    done = !GameApi.IsCarryingBody();
                    if (done)
                    {
                        bodiesFinished++;
                        ModLog.Info($"Corpos: corpo no crematório ({bodiesFinished} nesta sessão)");
                        ResetGoal();
                        Status = "corpo cremado";
                        return TaskResult.Succeeded; // um ciclo completo
                    }
                    break;
                case Goal.CollectCrematorium:
                    done = GameApi.GetCraftState(targetUid) != CraftState.ReadyToCollect;
                    break;
                case Goal.UseDoor:
                    // Normalmente o jogo tira o controle (fade) e o bot pausa antes disso; aqui é o caso sem fade.
                    uint now = GameApi.GetPlayerNavArea();
                    done = (now != 0 && now != doorFromArea) || GameApi.DistanceTo(targetPos) > 10f;
                    if (!done && Now - stepStartedAt > DoorTimeout)
                    {
                        return SkipDoor("a porta não levou a lugar nenhum");
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
                    CreditCollected();
                }
                ModLog.Info($"Corpos: {GoalText()} — ok");
                return Replan(null);
            }
            if (goal != Goal.UseDoor && Now - stepStartedAt > InteractTimeout)
            {
                return Fail($"a interação não teve efeito (alvo: {GameApi.DescribeInteractionTarget()})");
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
                        // Receita já estava em andamento: só trabalhar.
                        StartWork();
                        return TaskResult.Running;
                    }
                    if (GameApi.StartAutopsyExtract(targetUid, partId, out reason))
                    {
                        ModLog.Info($"Corpos: extraindo {partId}");
                        StartWork();
                        return TaskResult.Running;
                    }
                    failedParts.Add(bodyUid + "|" + partId);
                    ModLog.Warn($"Corpos: não deu para extrair {partId}: {reason}. Pulando esse órgão.");
                    return Replan(null);

                case Goal.ExtractPocket:
                    if (GameApi.StartPocketExtract(targetUid, partId, out reason))
                    {
                        ModLog.Info($"Corpos: tirando {partId} (Outros)");
                        StartWork();
                        return TaskResult.Running;
                    }
                    failedParts.Add(bodyUid + "|" + partId);
                    ModLog.Warn($"Corpos: não deu para tirar {partId}: {reason}. Pulando esse item.");
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
                        ModLog.Info("Corpos: corpo retirado da mesa");
                        return Replan(null);
                    }
                    return Fail("não consegui tirar o corpo da mesa: " + reason);

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
                ModLog.Warn($"Baú: nada foi guardado ({reason}). Não uso esse baú de novo até religar o bot.");
                return Replan(null);
            }
            foreach (KeyValuePair<string, int> kv in moved)
            {
                ledger.TryGetValue(kv.Key, out int have);
                int left = have - kv.Value;
                if (left > 0) { ledger[kv.Key] = left; } else { ledger.Remove(kv.Key); }
            }
            ModLog.Info("Baú: guardado " + string.Join(", ", moved.Select(kv => $"{kv.Key} x{kv.Value}")) + $" (livres agora: {GameApi.PlayerFreeSlots()})");
            return Replan(null);
        }

        /// <summary>Soma ao registro o que entrou no inventário desde a foto (o que o bot recolheu).</summary>
        private void CreditCollected()
        {
            if (invBefore == null)
            {
                return;
            }
            Dictionary<string, int> after = GameApi.SnapshotPlayerItems();
            foreach (KeyValuePair<string, int> kv in after)
            {
                invBefore.TryGetValue(kv.Key, out int before);
                int gained = kv.Value - before;
                if (gained > 0)
                {
                    ledger.TryGetValue(kv.Key, out int have);
                    ledger[kv.Key] = have + gained;
                }
            }
            invBefore = null;
        }

        private void StartWork()
        {
            FaceTarget();
            GameApi.SetHoldAction(true);
            lastProgress = GameApi.GetCraftProgressTicks(targetUid);
            lastProgressAt = Now;
            GoTo(Step.Work);
        }

        private TaskResult TickFill()
        {
            string id = GameApi.GetObjectDefId(targetUid);
            if (id != "grave_body")   // virou grave_ground (ou o objeto foi trocado): cova fechada
            {
                GameApi.SetHoldAction(false);
                buriedSpots.RemoveAll(sp => Vector3.Distance(sp, targetPos) < SameSpot);
                bodiesFinished++;
                ModLog.Info($"Corpos: corpo enterrado ({bodiesFinished} nesta sessão)");
                ResetGoal();
                Status = "corpo enterrado";
                return TaskResult.Succeeded; // um ciclo completo
            }
            Status = $"{GoalText()}: trabalhando ({Now - stepStartedAt:0}s)";
            if (Now - stepStartedAt > FillTimeout)
            {
                GameApi.SetHoldAction(false);
                return Fail($"a cova não fechou em {FillTimeout:0}s (pá no cinto? energia? alvo do jogo: {GameApi.DescribeInteractionTarget()})");
            }
            return TaskResult.Running;
        }

        private TaskResult TickWork()
        {
            if (goal == Goal.FillGrave)
            {
                return TickFill();
            }
            // Terminou quando a receita saiu da fila (ou a cova virou outra coisa).
            bool finished = !GameApi.IsCraftActive(targetUid);
            if (finished)
            {
                GameApi.SetHoldAction(false);
                ModLog.Info($"Corpos: {GoalText()} concluído");
                CreditCollected();
                return Replan(null);
            }

            int progress = GameApi.GetCraftProgressTicks(targetUid);
            if (progress != lastProgress)
            {
                lastProgress = progress;
                lastProgressAt = Now;
            }
            Status = $"{GoalText()}: trabalhando (progresso {Math.Max(progress, 0)})";
            if (Now - lastProgressAt > settings.WorkStallSeconds.Value)
            {
                GameApi.SetHoldAction(false);
                return Fail($"o trabalho não avança há {settings.WorkStallSeconds.Value:0}s (ferramenta no cinto? energia? alvo do jogo: {GameApi.DescribeInteractionTarget()})");
            }
            return TaskResult.Running;
        }

        // ------------------------------------------------------------------ utilitários

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
                    reason = "o corpo não está mais no chão";
                }
            }
            else if (!GameApi.ObjectExists(targetUid))
            {
                reason = "o objeto alvo sumiu";
            }
            return reason == null;
        }

        /// <summary>Porta que não funcionou: marca como quebrada e tenta outra rota, em vez de desligar o bot.</summary>
        private TaskResult SkipDoor(string why)
        {
            ModLog.Warn($"Corpos: porta {WorldObjectRef.ShortUid(targetUid)} ignorada nesta sessão — {why}");
            nav.MarkDoorBroken(targetUid);
            return Replan(null);
        }

        private TaskResult FailOrSkipDoorResult(string why) => goal == Goal.UseDoor ? SkipDoor(why) : Fail(why);

        private bool FailOrSkipDoor(string why) => FailOrSkipDoorResult(why) != TaskResult.Failed;

        private TaskResult Replan(string why)
        {
            if (why != null)
            {
                ModLog.Info($"Corpos: replanejando ({why})");
            }
            GameApi.SetHoldAction(false);
            ResetGoal();
            return TaskResult.Running;
        }

        private TaskResult Fail(string why)
        {
            planFailed = true;
            GameApi.ReleaseAllVirtualKeys();
            ModLog.Warn($"Corpos: {GoalText()} falhou — {why}");
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
            ? $"atravessar porta ({travelDoors} no caminho) para {travelPurpose}"
            : GoalText(goal, partId);

        private static string GoalText(Goal g, string part)
        {
            switch (g)
            {
                case Goal.PickUpBody: return "pegar corpo do chão";
                case Goal.PutOnTable: return "colocar corpo na mesa";
                case Goal.ExtractOrgan: return part != null ? $"extrair {part}" : "continuar autópsia";
                case Goal.ExtractPocket: return $"tirar {part}";
                case Goal.TakeBody: return "tirar corpo da mesa";
                case Goal.Bury: return "enterrar corpo";
                case Goal.PickUpFromPallet: return "pegar corpo do palete";
                case Goal.Cremate: return "levar corpo ao crematório";
                case Goal.CollectCrematorium: return "recolher o crematório";
                case Goal.UseDoor: return "atravessar porta";
                case Goal.InspectCrematorium: return "checar o crematório";
                case Goal.FillGrave: return "fechar a cova";
                case Goal.DepositChest: return "guardar itens no baú";
                default: return "-";
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using AutoKeeper.Config;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot.Tasks
{
    /// <summary>
    /// Rotina MVP "processar corpos" (layout real do necrotério, dump do jogo 1.007):
    ///   palete (pallet_corpse) ou chão → mesa de autópsia livre → extrair órgãos (config) → tirar corpo →
    ///   destino: crematório (padrão), deixar na mesa, ou cova vazia (experimental, mesma área).
    /// Cada Tick decide UM objetivo a partir do estado do mundo (replaneja sozinho depois de pausa/abort)
    /// e o executa em passos curtos: andar → mirar → apertar E / iniciar receita → segurar Ação até terminar.
    /// Usa só a GameApi; nada de teleporte, spawn ou edição de save.
    /// </summary>
    internal sealed class ProcessBodiesTask : ITask
    {
        private enum Goal
        {
            None,
            PickUpBody,
            PutOnTable,
            ExtractOrgan,
            TakeBody,
            Bury,
            PickUpFromPallet,
            Cremate,
            CollectCrematorium,
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

        private const float NearEnough = 2.2f;        // distância do ponto de parada considerada "cheguei"
        private const float AimTimeout = 2.5f;
        private const float InteractTimeout = 3f;
        private const int MaxMoveRetries = 2;

        private readonly Settings settings;

        // Memória entre objetivos (não depende de tipos do jogo).
        private readonly HashSet<string> autopsyDone = new HashSet<string>();        // corpos que já passaram pela mesa
        private readonly HashSet<string> failedOrgans = new HashSet<string>();       // "corpo|órgão" que o jogo recusou
        private int bodiesFinished;

        // Objetivo atual.
        private Goal goal;
        private Step step;
        private string targetUid;
        private bool targetIsGround;
        private Vector3 targetPos;
        private Vector3 standSpot;
        private Vector2 standFacing;
        private string organId;
        private string bodyUid;
        private float stepStartedAt;
        private int moveRetries;
        private int lastProgress;
        private float lastProgressAt;
        private bool planFailed;
        private bool nudged;

        public ProcessBodiesTask(Settings settings)
        {
            this.settings = settings;
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
            if (GameApi.IsCarryingBody())
            {
                if (!GameApi.CarriedBodyIsPlain())
                {
                    reason = "carregando corpo de zumbi/demônio (fora do MVP)";
                    return false;
                }
                reason = null;
                return true;
            }
            if (GameApi.IsCarryingAnything())
            {
                reason = "mãos ocupadas com outro item";
                return false;
            }
            if (PlanNext(dryRun: true))
            {
                reason = null;
                return true;
            }
            reason = "sem corpos para processar por perto";
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

        // ------------------------------------------------------------------ planejamento

        /// <summary>Escolhe o próximo objetivo a partir do estado do mundo. Em dryRun só responde se há o que fazer.</summary>
        private bool PlanNext(bool dryRun)
        {
            bool wantsOrgans = settings.SelectedOrganTypes().Count > 0;
            BodyDestination dest = settings.Destination.Value;
            List<WorldObjectRef> tables = GameApi.FindObjects(ObjectKind.AutopsyTable, Radius)
                .Where(t => !GameApi.HasOtherWorker(t.Uid)) // mesa com zumbi/NPC trabalhando: não mexer
                .ToList();
            WorldObjectRef? crem = dest == BodyDestination.Crematorium ? FirstCrematorium() : null;
            CraftState cremState = crem.HasValue ? GameApi.GetCraftState(crem.Value.Uid) : CraftState.Unknown;
            bool cremFree = crem.HasValue && cremState == CraftState.Idle && !GameApi.ObjectHasBody(crem.Value.Uid);

            // 1) Carregando um corpo: mesa (se ainda não fez autópsia) ou destino.
            if (GameApi.IsCarryingBody())
            {
                string carried = GameApi.GetCarriedBodyUid();
                WorldObjectRef? freeTable = FirstFreeTable(tables);
                if (wantsOrgans && carried != null && !autopsyDone.Contains(carried) && freeTable.HasValue)
                {
                    return Begin(dryRun, Goal.PutOnTable, freeTable.Value.Uid, freeTable.Value.Position, false, carried);
                }
                switch (dest)
                {
                    case BodyDestination.Crematorium:
                        if (!crem.HasValue)
                        {
                            return dryRun || Fail("carregando corpo, mas não há crematório nesta área") != TaskResult.Failed;
                        }
                        if (cremFree)
                        {
                            return Begin(dryRun, Goal.Cremate, crem.Value.Uid, crem.Value.Position, false, carried);
                        }
                        return Wait(dryRun, "aguardando o crematório ficar livre");
                    case BodyDestination.Grave:
                        WorldObjectRef? grave = FirstFreeGrave();
                        if (grave.HasValue)
                        {
                            return Begin(dryRun, Goal.Bury, grave.Value.Uid, grave.Value.Position, false, carried);
                        }
                        return dryRun || Fail("carregando corpo, mas não há cova vazia (grave_empty) livre nesta área") != TaskResult.Failed;
                    default:
                        if (freeTable.HasValue)
                        {
                            return Begin(dryRun, Goal.PutOnTable, freeTable.Value.Uid, freeTable.Value.Position, false, carried);
                        }
                        return dryRun || Fail("carregando corpo, mas não há mesa de autópsia livre") != TaskResult.Failed;
                }
            }

            // 2) Crematório terminou: recolher o resultado antes de qualquer coisa (libera para o próximo corpo).
            if (crem.HasValue && cremState == CraftState.ReadyToCollect)
            {
                return Begin(dryRun, Goal.CollectCrematorium, crem.Value.Uid, crem.Value.Position, false, null);
            }

            // 3) Mesas com corpo comum.
            foreach (WorldObjectRef t in tables)
            {
                if (!GameApi.ObjectHasPlainBody(t.Uid))
                {
                    continue;
                }
                string body = GameApi.GetBodyUidInObject(t.Uid);

                // 3a) Receita em andamento (extração): trabalhar nela.
                if (GameApi.IsCraftActive(t.Uid))
                {
                    return Begin(dryRun, Goal.ExtractOrgan, t.Uid, t.Position, false, body, organ: null);
                }

                // 3b) Ainda há órgão desejado para extrair.
                if (wantsOrgans && body != null && !autopsyDone.Contains(body))
                {
                    string organ = NextOrgan(t.Uid, body);
                    if (organ != null)
                    {
                        return Begin(dryRun, Goal.ExtractOrgan, t.Uid, t.Position, false, body, organ);
                    }
                }

                // 3c) Autópsia concluída: tirar da mesa só se o destino estiver disponível agora.
                bool destReady = (dest == BodyDestination.Crematorium && cremFree)
                    || (dest == BodyDestination.Grave && FirstFreeGrave().HasValue);
                if (destReady)
                {
                    return Begin(dryRun, Goal.TakeBody, t.Uid, t.Position, false, body);
                }
            }

            // 4) Buscar um corpo novo (palete primeiro, depois chão) se houver mesa livre
            //    ou, sem autópsia, se o destino estiver disponível.
            bool canReceive = wantsOrgans
                ? FirstFreeTable(tables).HasValue
                : (dest == BodyDestination.Crematorium && cremFree) || (dest == BodyDestination.Grave && FirstFreeGrave().HasValue);
            if (!canReceive)
            {
                return false;
            }
            foreach (WorldObjectRef pallet in GameApi.FindObjects(ObjectKind.MorguePallet, Radius))
            {
                if (GameApi.ObjectHasPlainBody(pallet.Uid))
                {
                    return Begin(dryRun, Goal.PickUpFromPallet, pallet.Uid, pallet.Position, false, null);
                }
            }
            List<GroundItemRef> bodies = GameApi.FindGroundBodies(Radius);
            if (bodies.Count > 0)
            {
                GroundItemRef b = bodies[0];
                return Begin(dryRun, Goal.PickUpBody, b.Uid, b.Position, true, null);
            }
            return false;
        }

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

        private WorldObjectRef? FirstCrematorium()
        {
            foreach (WorldObjectRef c in GameApi.FindObjects(ObjectKind.Crematorium, Radius))
            {
                if (!GameApi.HasOtherWorker(c.Uid))
                {
                    return c;
                }
            }
            return null;
        }

        private static WorldObjectRef? FirstFreeTable(List<WorldObjectRef> tables)
        {
            foreach (WorldObjectRef t in tables)
            {
                if (!GameApi.ObjectHasBody(t.Uid) && !GameApi.IsCraftActive(t.Uid))
                {
                    return t;
                }
            }
            return null;
        }

        private WorldObjectRef? FirstFreeGrave()
        {
            foreach (WorldObjectRef g in GameApi.FindObjects(ObjectKind.EmptyGrave, Radius))
            {
                if (!GameApi.ObjectHasBody(g.Uid) && !GameApi.IsCraftActive(g.Uid) && !GameApi.HasOtherWorker(g.Uid))
                {
                    return g;
                }
            }
            return null;
        }

        private string NextOrgan(string tableUid, string body)
        {
            HashSet<string> allowed = settings.SelectedOrganTypes();
            foreach (string organ in GameApi.GetExtractableOrgans(tableUid, false, allowed))
            {
                if (!failedOrgans.Contains(body + "|" + organ))
                {
                    return organ;
                }
            }
            return null;
        }

        private bool Begin(bool dryRun, Goal g, string uid, Vector3 pos, bool ground, string body, string organ = null)
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
            organId = organ;
            moveRetries = 0;

            if (ground)
            {
                standSpot = GameApi.GetApproachSpot(pos, 0.8f);
                standFacing = Vector2.zero;
            }
            else if (!GameApi.TryGetStandSpot(uid, out standSpot, out standFacing))
            {
                return Fail($"alvo {WorldObjectRef.ShortUid(uid)} sumiu") != TaskResult.Failed;
            }

            ModLog.Info($"Corpos: objetivo {GoalText()}");
            GoTo(Step.Move);
            if (GameApi.DistanceTo(standSpot) <= NearEnough)
            {
                GoTo(Step.Aim); // já está perto
            }
            else if (!GameApi.StartMoveTo(standSpot))
            {
                return Fail("o jogo recusou o caminho até o alvo") != TaskResult.Failed;
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
            Status = $"{GoalText()}: andando ({GameApi.DistanceTo(standSpot):0.0} m)";
            MoveState ms = GameApi.GetMoveState();
            float dist = GameApi.DistanceTo(standSpot);

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
                    Vector3 dest = moveRetries == 2 ? targetPos : standSpot;
                    ModLog.Debug($"Movimento falhou; nova tentativa {moveRetries} para {dest}");
                    GameApi.StartMoveTo(dest);
                    stepStartedAt = Now;
                    return TaskResult.Running;
                }
                return Fail("não achei caminho até o alvo");
            }
            if (Now - stepStartedAt > settings.MoveTimeoutSeconds.Value)
            {
                GameApi.StopMoving();
                return Fail("demorei demais andando até o alvo");
            }
            return TaskResult.Running;
        }

        private TaskResult TickAim()
        {
            if (!TargetStillValid(out string gone))
            {
                return Replan(gone);
            }

            // Objetivos sem tecla: autópsia/tirar corpo/enterro usam a ação de UI direto (perto do objeto).
            if (goal == Goal.ExtractOrgan || goal == Goal.TakeBody || goal == Goal.Bury)
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
                return Fail($"não consegui mirar no alvo (o jogo mira em: {GameApi.DescribeInteractionTarget()})");
            }
            return TaskResult.Running;
        }

        private TaskResult TickAwaitInteract()
        {
            bool done;
            switch (goal)
            {
                case Goal.PickUpBody:
                    done = GameApi.IsCarryingBody();
                    break;
                case Goal.PickUpFromPallet:
                    done = GameApi.IsCarryingBody();
                    break;
                case Goal.PutOnTable:
                    done = !GameApi.IsCarryingBody() && GameApi.ObjectHasBody(targetUid);
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
                default:
                    done = true;
                    break;
            }
            if (done)
            {
                ModLog.Info($"Corpos: {GoalText()} — ok");
                return Replan(null);
            }
            if (Now - stepStartedAt > InteractTimeout)
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
                    if (organId == null)
                    {
                        // Receita já estava em andamento: só trabalhar.
                        StartWork();
                        return TaskResult.Running;
                    }
                    if (GameApi.StartAutopsyExtract(targetUid, organId, out reason))
                    {
                        ModLog.Info($"Corpos: extraindo {organId}");
                        StartWork();
                        return TaskResult.Running;
                    }
                    failedOrgans.Add(bodyUid + "|" + organId);
                    ModLog.Warn($"Corpos: não deu para extrair {organId}: {reason}. Pulando esse órgão.");
                    return Replan(null);

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

                case Goal.Bury:
                    if (GameApi.StartBurial(targetUid, settings.GraveCraftId.Value?.Trim(), out reason))
                    {
                        if (GameApi.IsCarryingBody())
                        {
                            // Segurar Ação com algo na cabeça faz o jogo largar o item no chão: não arriscar.
                            return Fail("a receita de enterro começou mas o corpo continua na cabeça — confira no dump/log qual receita usar");
                        }
                        ModLog.Info("Corpos: enterro iniciado");
                        StartWork();
                        return TaskResult.Running;
                    }
                    return Fail("não consegui iniciar o enterro: " + reason);

                default:
                    return Replan(null);
            }
        }

        private void StartWork()
        {
            FaceTarget();
            GameApi.SetHoldAction(true);
            lastProgress = GameApi.GetCraftProgressTicks(targetUid);
            lastProgressAt = Now;
            GoTo(Step.Work);
        }

        private TaskResult TickWork()
        {
            // Terminou quando a receita saiu da fila (ou a cova virou outra coisa).
            bool finished = !GameApi.IsCraftActive(targetUid)
                || (goal == Goal.Bury && GameApi.GetObjectDefId(targetUid) != "grave_empty");
            if (finished)
            {
                GameApi.SetHoldAction(false);
                if (goal == Goal.Bury)
                {
                    bodiesFinished++;
                    ModLog.Info($"Corpos: corpo enterrado ({bodiesFinished} nesta sessão)");
                    ResetGoal();
                    Status = "corpo enterrado";
                    return TaskResult.Succeeded; // um ciclo completo
                }
                ModLog.Info($"Corpos: {GoalText()} concluído");
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
            Status = why;
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
            organId = null;
            bodyUid = null;
        }

        private void GoTo(Step s)
        {
            step = s;
            stepStartedAt = Now;
            nudged = false;
        }

        private string GoalText()
        {
            switch (goal)
            {
                case Goal.PickUpBody: return "pegar corpo do chão";
                case Goal.PutOnTable: return "colocar corpo na mesa";
                case Goal.ExtractOrgan: return organId != null ? $"extrair {organId}" : "continuar autópsia";
                case Goal.TakeBody: return "tirar corpo da mesa";
                case Goal.Bury: return "enterrar corpo";
                case Goal.PickUpFromPallet: return "pegar corpo do palete";
                case Goal.Cremate: return "levar corpo ao crematório";
                case Goal.CollectCrematorium: return "recolher o crematório";
                default: return "-";
            }
        }
    }
}

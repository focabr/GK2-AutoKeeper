using System;
using System.Collections.Generic;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>Tipo de objeto do mundo que interessa ao bot.</summary>
    internal enum ObjectKind
    {
        AutopsyTable,
        EmptyGrave,
        MorguePallet,
        Crematorium,
        Chest,
        GraveBody,
        GravePlace,
    }

    /// <summary>Estado resumido da receita de um objeto (sem expor o enum do jogo ao bot).</summary>
    internal enum CraftState
    {
        Unknown,
        Idle,
        Running,
        ReadyToCollect,
        Other,
    }

    /// <summary>Handle opaco para um objeto do mundo (o bot nunca guarda tipos do jogo).</summary>
    internal readonly struct WorldObjectRef
    {
        public readonly string Uid;
        public readonly string DefId;
        public readonly Vector3 Position;

        public WorldObjectRef(string uid, string defId, Vector3 position)
        {
            Uid = uid;
            DefId = defId;
            Position = position;
        }

        public override string ToString() => $"{DefId}#{ShortUid(Uid)}";

        internal static string ShortUid(string uid) => string.IsNullOrEmpty(uid) || uid.Length < 6 ? uid : uid.Substring(0, 6);
    }

    /// <summary>Handle opaco para um item no chão (ex.: corpo).</summary>
    internal readonly struct GroundItemRef
    {
        public readonly string Uid;
        public readonly string ItemId;
        public readonly Vector3 Position;

        public GroundItemRef(string uid, string itemId, Vector3 position)
        {
            Uid = uid;
            ItemId = itemId;
            Position = position;
        }

        public override string ToString() => $"{ItemId}#{WorldObjectRef.ShortUid(Uid)}";
    }

    internal enum MoveState
    {
        Idle,
        Moving,
        Arrived,
        Failed,
    }

    /// <summary>
    /// Parte 4: consultas ao mundo (cena atual), movimento com o pathfinding do próprio jogo e mira.
    /// Somente o que o jogador poderia fazer: andar, virar-se, olhar o que está no chão/nas mesas.
    /// </summary>
    internal static partial class GameApi
    {
        private const float PlayerWalkSpeed = 3.3f; // mesma velocidade que o jogo usa ao mover o jogador sozinho

        private static AccessTools.FieldRef<MovementComponent, MovementComponent.Status> movementStatusRef;
        private static bool moveRequested;

        // ------------------------------------------------------------------ helpers internos

        private static GameSceneData CurrentScene() => MainGame.WorldData.GetGameSceneDataById(MainGame.PlayerData.currentGameSceneId);

        private static WgoData FindWgoByUid(string uid)
        {
            if (string.IsNullOrEmpty(uid))
            {
                return null;
            }
            WgoData w = MainGame.WorldData.GetWgoData(new SGuid(uid));
            return w != null && w.WorldId == MainGame.PlayerData.currentGameSceneId ? w : null;
        }

        private static DropData FindDropByUid(string uid)
        {
            foreach (DropData d in CurrentScene().droppedItems)
            {
                if (d?.Item != null && d.UniqueId != null && d.UniqueId.Id == uid)
                {
                    return d;
                }
            }
            return null;
        }

        private static bool IsBodyItem(Item item)
        {
            if (item == null || item.IsEmpty || item.Definition == null)
            {
                return false;
            }
            List<string> groups = item.Definition.itemGroupIds;
            return groups != null && groups.Contains("body");
        }

        /// <summary>Corpo "comum": exclui zumbis e corpos com demônio (o MVP não mexe neles).</summary>
        private static bool IsPlainBody(Item item)
        {
            return IsBodyItem(item)
                && item.Definition.itemGroupIds.Contains("corpse")
                && !item.Definition.itemGroupIds.Contains("zombie")
                && !item.HasItemsByItemType(ItemType.Demon);
        }

        private static Item FindBodyInInventory(WgoData w)
        {
            foreach (Item it in w.Inventory.Data.Inventory)
            {
                if (IsBodyItem(it))
                {
                    return it;
                }
            }
            return null;
        }

        private static Item CarriedBody()
        {
            foreach (Item it in MainGame.PlayerData.OverheadItems)
            {
                if (IsBodyItem(it))
                {
                    return it;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ consultas

        /// <summary>Corpos comuns no chão da cena atual, do mais perto ao mais longe, até <paramref name="maxDistance"/>.</summary>
        public static List<GroundItemRef> FindGroundBodies(float maxDistance) => Safe(() =>
        {
            var result = new List<GroundItemRef>();
            Vector3 p = MainGame.PlayerData.position.Value;
            foreach (DropData d in CurrentScene().droppedItems)
            {
                if (d == null || d.IsRemoving || !IsPlainBody(d.Item))
                {
                    continue;
                }
                if (Vector3.Distance(d.Position, p) <= maxDistance)
                {
                    result.Add(new GroundItemRef(d.UniqueId.Id, d.Id, d.Position));
                }
            }
            result.Sort((a, b) => Vector3.Distance(a.Position, p).CompareTo(Vector3.Distance(b.Position, p)));
            return result;
        }, new List<GroundItemRef>(), nameof(FindGroundBodies));

        /// <summary>Objetos de um tipo na cena atual, do mais perto ao mais longe.</summary>
        public static List<WorldObjectRef> FindObjects(ObjectKind kind, float maxDistance) => Safe(() =>
        {
            var result = new List<WorldObjectRef>();
            Vector3 p = MainGame.PlayerData.position.Value;
            foreach (WgoData w in CurrentScene().wgoDataList)
            {
                if (w?.Definition == null || !w.IsInteractable || !MatchesKind(w, kind))
                {
                    continue;
                }
                if (Vector3.Distance(w.Position, p) <= maxDistance)
                {
                    result.Add(new WorldObjectRef(w.UniqueId.Id, w.id, w.Position));
                }
            }
            result.Sort((a, b) => Vector3.Distance(a.Position, p).CompareTo(Vector3.Distance(b.Position, p)));
            return result;
        }, new List<WorldObjectRef>(), nameof(FindObjects));

        private static bool MatchesKind(WgoData w, ObjectKind kind)
        {
            switch (kind)
            {
                case ObjectKind.AutopsyTable:
                    return w.Definition.interactionType == WGODef.InteractionType.Autopsy;
                case ObjectKind.EmptyGrave:
                    return w.id == "grave_empty"; // GameConsts.EMPTY_GRAVE_WGO_ID
                case ObjectKind.MorguePallet:
                    return w.Definition.wgoGroup == "morgue_pallets"; // pallet_corpse_1/2 (dump 1.007)
                case ObjectKind.Crematorium:
                    return w.Definition.interactionType == WGODef.InteractionType.Crematorium;
                case ObjectKind.GraveBody:
                    return w.id == "grave_body"; // cova com corpo ainda por fechar (trabalho com pá)
                case ObjectKind.GravePlace:
                    return w.id == "grave_empty_place"; // cova marcada pelo construtor, ainda por cavar (pá → grave_empty)
                case ObjectKind.Chest:
                    return w.Definition.interactionType == WGODef.InteractionType.Chest
                        && w.Definition.inventorySize > 0
                        && w.Definition.conveyorType == ConveyorElementType.None // baús de esteira/jardim/vinho são outra coisa
                        && string.IsNullOrEmpty(w.CustomTag)                    // baús de missão (ex.: chest_resurrection) têm tag
                        && !w.id.StartsWith("garden_bags_storage");
                default:
                    return false;
            }
        }

        /// <summary>Id atual do objeto (muda quando a cova vira grave_body, por exemplo). null se sumiu.</summary>
        public static string GetObjectDefId(string uid) => Safe(() => FindWgoByUid(uid)?.id, null, nameof(GetObjectDefId));

        public static bool ObjectExists(string uid) => Safe(() => FindWgoByUid(uid) != null, false, nameof(ObjectExists));

        public static bool GroundItemExists(string uid) => Safe(() => FindDropByUid(uid) != null, false, nameof(GroundItemExists));

        /// <summary>O objeto tem um corpo dentro (mesa, cova)?</summary>
        public static bool ObjectHasBody(string uid) => Safe(() =>
        {
            WgoData w = FindWgoByUid(uid);
            return w != null && FindBodyInInventory(w) != null;
        }, false, nameof(ObjectHasBody));

        /// <summary>O corpo dentro do objeto é comum (não zumbi/demônio)?</summary>
        public static bool ObjectHasPlainBody(string uid) => Safe(() =>
        {
            WgoData w = FindWgoByUid(uid);
            return w != null && IsPlainBody(FindBodyInInventory(w));
        }, false, nameof(ObjectHasPlainBody));

        /// <summary>Id único do corpo dentro do objeto (para acompanhar o mesmo corpo entre etapas).</summary>
        public static string GetBodyUidInObject(string uid) => Safe(() =>
        {
            WgoData w = FindWgoByUid(uid);
            return w == null ? null : FindBodyInInventory(w)?.UniqueId?.Id;
        }, null, nameof(GetBodyUidInObject));

        /// <summary>Receita em andamento ou na fila (o jogador precisa trabalhar ou esperar)?</summary>
        public static bool IsCraftActive(string uid) => Safe(() =>
        {
            CraftComponent cc = FindWgoByUid(uid)?.CraftComponent;
            return cc != null && (cc.IsStarted || cc.HasCraftsInQueue);
        }, false, nameof(IsCraftActive));

        /// <summary>Estado da receita do objeto: parado, rodando, pronto para recolher (auto-craft terminado).</summary>
        public static CraftState GetCraftState(string uid) => Safe(() =>
        {
            CraftComponent cc = FindWgoByUid(uid)?.CraftComponent;
            if (cc == null)
            {
                return CraftState.Unknown;
            }
            switch (cc.Status)
            {
                case CraftComponentStatus.ReadyToFinishAutoCraft:
                    return CraftState.ReadyToCollect;
                case CraftComponentStatus.Started:
                case CraftComponentStatus.FinishDelayed:
                case CraftComponentStatus.ReadyToStartCraft:
                case CraftComponentStatus.QueueDelayed:
                    return CraftState.Running;
                case CraftComponentStatus.None:
                case CraftComponentStatus.Finished:
                case CraftComponentStatus.Canceled:
                    return cc.HasCraftsInQueue ? CraftState.Running : CraftState.Idle;
                default:
                    return CraftState.Other;
            }
        }, CraftState.Unknown, nameof(GetCraftState));

        /// <summary>Progresso da receita atual em ticks (para detectar trabalho travado). -1 se não houver.</summary>
        public static int GetCraftProgressTicks(string uid) => Safe(() =>
        {
            CraftElementBase e = FindWgoByUid(uid)?.CraftComponent?.CurrentCraftElement;
            return e == null ? -1 : e.ProgressTicks;
        }, -1, nameof(GetCraftProgressTicks));

        /// <summary>Outro trabalhador (zumbi/NPC) está designado no objeto? O próprio jogador não conta.</summary>
        public static bool HasOtherWorker(string uid) => Safe(() =>
        {
            IWorker w = FindWgoByUid(uid)?.Worker;
            return w != null && !(w is PlayerController);
        }, false, nameof(HasOtherWorker));

        public static bool IsCarryingBody() => Safe(() => CarriedBody() != null, false, nameof(IsCarryingBody));

        public static bool IsCarryingAnything() => Safe(() => MainGame.PlayerData.HasOverheadItem, false, nameof(IsCarryingAnything));

        public static string GetCarriedBodyUid() => Safe(() => CarriedBody()?.UniqueId?.Id, null, nameof(GetCarriedBodyUid));

        public static bool CarriedBodyIsPlain() => Safe(() => IsPlainBody(CarriedBody()), false, nameof(CarriedBodyIsPlain));

        public static float DistanceTo(Vector3 p) => Safe(() => Vector3.Distance(MainGame.PlayerData.position.Value, p), float.MaxValue, nameof(DistanceTo));

        // ------------------------------------------------------------------ pontos de interação

        /// <summary>
        /// Onde o jogador deve ficar para usar o objeto: o dock point livre mais próximo (preferindo os de jogador,
        /// não os de zumbi). Se o objeto ainda não foi instanciado na tela, devolve a posição dele.
        /// </summary>
        private const float DockClearRadius = 0.2f;   // raio do jogador para checar se o ponto de trabalho está livre
        private static readonly HashSet<string> LoggedDockTargets = new HashSet<string>();

        private const float DockCrowdDistance = 1.0f; // outro objeto grande mais perto que isso do ponto = apertado

        /// <summary>Id de outro objeto interativo (mesa, baú, palete, crematório…) colado no ponto, ou null.</summary>
        private static string CrowdingObject(Vector3 dock, string ownerUid)
        {
            foreach (WgoData o in CurrentScene().wgoDataList)
            {
                if (o?.Definition == null || !o.IsInteractable || o.UniqueId.Id == ownerUid || o.id.Contains("wisp"))
                {
                    continue;
                }
                Vector3 d = o.Position - dock;
                d.y = 0f;
                if (d.sqrMagnitude < DockCrowdDistance * DockCrowdDistance)
                {
                    return o.id;
                }
            }
            return null;
        }

        private static bool SafeBool(Func<bool> f)
        {
            try { return f(); } catch { return true; } // na dúvida, não descarta o ponto
        }

        public static bool TryGetStandSpot(string uid, out Vector3 spot, out Vector2 facing)
        {
            Vector3 s = Vector3.zero;
            Vector2 f = Vector2.zero;
            bool ok = Safe(() =>
            {
                WgoData w = FindWgoByUid(uid);
                if (w == null)
                {
                    return false;
                }
                s = w.Position;
                Wgo view = GameScene.GetWgoViewGlobal(w.UniqueId);
                IReadOnlyList<DockPoint> docks = view != null ? view.DockPoints : null;
                if (docks == null || docks.Count == 0)
                {
                    return true;
                }
                Vector3 p = MainGame.PlayerData.position.Value;
                RecastGraph rg = PlayerNavGraph() as RecastGraph;
                DockPoint best = null;
                float bestScore = float.MaxValue;
                var diag = new System.Text.StringBuilder();
                foreach (DockPoint d in docks)
                {
                    if (d == null || !d.gameObject.activeInHierarchy || d.DontUseForWorkerPlacement)
                    {
                        continue;
                    }
                    // Ponto de trabalho que o jogador não alcança: outro objeto em cima (colisor sólido de outro Wgo,
                    // ex.: baú encostado) ou fora do navmesh. O bot anda por caminho roteirizado e passaria por cima.
                    bool free = SafeBool(() => d.IsReachable(DockClearRadius));
                    bool onMesh = rg == null || SafeBool(() => d.IsReachable(rg));
                    Vector3 dp = d.transform.position;
                    // Espremido entre objetos (ex.: vão entre as duas mesas): o jogador não passa, o caminho roteirizado passa.
                    string crowd = CrowdingObject(dp, w.UniqueId.Id);
                    diag.Append($" [{dp.x:0.00},{dp.z:0.00} {d.Direction}{(d.IsForZombie ? " zumbi" : "")}{(free ? "" : " BLOQUEADO")}"
                        + $"{(crowd != null ? " APERTADO por " + crowd : "")}{(onMesh ? "" : " fora-navmesh")}]");
                    float score = Vector3.Distance(dp, p) + (d.IsForZombie ? 1000f : 0f) + (free ? 0f : 500f)
                        + (crowd != null ? 300f : 0f) + (onMesh ? 0f : 5f);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = d;
                    }
                }
                if (best != null && LoggedDockTargets.Add(uid))
                {
                    Vector3 bp = best.transform.position;
                    ModLog.Info($"Pontos de trabalho de {w.id}:{diag} → escolhido {bp.x:0.00},{bp.z:0.00}");
                }
                if (best != null)
                {
                    s = best.transform.position;
                    f = best.Direction != Direction.None ? best.Direction.ConvertToVector2XZ() : Vector2.zero;
                }
                return true;
            }, false, nameof(TryGetStandSpot));
            spot = s;
            facing = f;
            return ok;
        }

        /// <summary>Ponto um pouco antes do item no chão, vindo do lado do jogador (para ficar de frente para ele).</summary>
        public static Vector3 GetApproachSpot(Vector3 target, float standOff) => Safe(() =>
        {
            Vector3 p = MainGame.PlayerData.position.Value;
            Vector3 dir = target - p;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return p;
            }
            return target - dir.normalized * standOff;
        }, target, nameof(GetApproachSpot));

        /// <summary>Vira o jogador para a direção (x,z) dada, como faria o analógico.</summary>
        public static void FaceDirection(Vector2 dirXZ) => Safe(() =>
        {
            MainGame.PlayerController.PhysicalBody.SetFacingDirection(dirXZ);
            return true;
        }, false, nameof(FaceDirection));

        /// <summary>Vira o jogador para um ponto do mundo.</summary>
        public static void FaceTowards(Vector3 target) => Safe(() =>
        {
            Vector3 d = target - MainGame.PlayerData.position.Value;
            MainGame.PlayerController.PhysicalBody.SetFacingDirection(new Vector2(d.x, d.z));
            return true;
        }, false, nameof(FaceTowards));

        /// <summary>O objeto é o alvo de interação atual (o que o E acionaria)? Itens grandes no chão têm prioridade no jogo.</summary>
        public static bool IsObjectUnderInteraction(string uid) => Safe(() =>
        {
            PlayerInteractionComponent pic = MainGame.PlayerController.PlayerInteractionComponent;
            if (pic.BigDropUnderInteraction != null)
            {
                return false;
            }
            Wgo w = pic.WgoUnderInteraction;
            return w != null && w.HasData && w.Data.UniqueId.Id == uid;
        }, false, nameof(IsObjectUnderInteraction));

        /// <summary>
        /// Uid do alvo de interação atual do jogo (objeto ou item grande no chão), ou null se não há alvo.
        /// Usado para nunca segurar Ação mirando outra coisa (Ação num baú = "pegar tudo").
        /// </summary>
        public static string GetInteractionTargetUid() => Safe(() =>
        {
            PlayerInteractionComponent pic = MainGame.PlayerController.PlayerInteractionComponent;
            if (pic.BigDropUnderInteraction?.Data != null)
            {
                return pic.BigDropUnderInteraction.Data.UniqueId.Id;
            }
            Wgo w = pic.WgoUnderInteraction;
            return w != null && w.HasData ? w.Data.UniqueId.Id : null;
        }, null, nameof(GetInteractionTargetUid));

        /// <summary>O item no chão é o alvo de interação atual?</summary>
        public static bool IsGroundItemUnderInteraction(string uid) => Safe(() =>
        {
            DropView v = MainGame.PlayerController.PlayerInteractionComponent.BigDropUnderInteraction;
            return v != null && v.Data != null && v.Data.UniqueId.Id == uid;
        }, false, nameof(IsGroundItemUnderInteraction));

        /// <summary>Descrição do alvo atual de interação (para log/overlay).</summary>
        public static string DescribeInteractionTarget() => Safe(() =>
        {
            PlayerInteractionComponent pic = MainGame.PlayerController.PlayerInteractionComponent;
            if (pic.BigDropUnderInteraction?.Data != null)
            {
                return "chão: " + pic.BigDropUnderInteraction.Data.Id;
            }
            return pic.WgoUnderInteraction != null && pic.WgoUnderInteraction.HasData ? pic.WgoUnderInteraction.Data.id : "-";
        }, "?", nameof(DescribeInteractionTarget));

        // ------------------------------------------------------------------ movimento

        /// <summary>
        /// Anda até <paramref name="target"/> usando o mesmo pathfinding (grafo Recast da cena) que o jogo usa
        /// para mover o jogador em cenas roteirizadas/pesca. Não teleporta.
        /// </summary>
        public static bool StartMoveTo(Vector3 target) => Safe(() =>
        {
            PlayerController pc = MainGame.PlayerController;
            string scene = MainGame.PlayerData.currentGameSceneId;
            MovementComponent mc = pc.MovementComponent;
            moveRequested = true;
            MovementComponent.StartPathResult r = mc.StartPath(target, scene, scene, MovementType.Recast, PlayerWalkSpeed, "", null,
                pc.PlayerLocalAreaMovement.Seeker);
            if (r == MovementComponent.StartPathResult.AlreadyAtDestinationPoint)
            {
                moveRequested = false;
                return true;
            }
            if (r != MovementComponent.StartPathResult.Started)
            {
                moveRequested = false;
                ModLog.Warn($"Movimento recusado pelo jogo: {r}");
                return false;
            }
            return true;
        }, false, nameof(StartMoveTo));

        /// <summary>Estado do último movimento pedido pelo bot.</summary>
        public static MoveState GetMoveState() => Safe(() =>
        {
            MovementComponent mc = MainGame.PlayerController.MovementComponent;
            if (movementStatusRef == null)
            {
                movementStatusRef = AccessTools.FieldRefAccess<MovementComponent, MovementComponent.Status>("status");
            }
            MovementComponent.Status st = movementStatusRef(mc);
            if (st != MovementComponent.Status.None)
            {
                return MoveState.Moving;
            }
            if (!moveRequested)
            {
                return MoveState.Idle;
            }
            // Terminou: sucesso, ou o jogo não achou caminho (status volta a None sem Completion).
            return mc.Completion == MovementComponent.CompletionState.Success ? MoveState.Arrived : MoveState.Failed;
        }, MoveState.Failed, nameof(GetMoveState));

        /// <summary>Para o movimento iniciado pelo bot e devolve a física normal ao jogador.</summary>
        public static void StopMoving() => Safe(() =>
        {
            if (!moveRequested)
            {
                return true;
            }
            moveRequested = false;
            MovementComponent mc = MainGame.PlayerController.MovementComponent;
            // ForceStop também chama OnPathComplete do jogador (volta a física dinâmica), inclusive
            // quando o jogo não achou caminho e deixou o corpo em modo cinemático.
            mc.ForceStop();
            return true;
        }, false, nameof(StopMoving));

        /// <summary>Marca o movimento como encerrado sem ForceStop (chegou normalmente).</summary>
        public static void ClearMoveRequest()
        {
            moveRequested = false;
        }
    }
}

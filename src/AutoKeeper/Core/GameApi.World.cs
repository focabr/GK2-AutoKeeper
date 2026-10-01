using System;
using System.Collections.Generic;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>Kind of world object the bot cares about.</summary>
    internal enum ObjectKind
    {
        AutopsyTable,
        EmptyGrave,
        MorguePallet,
        Crematorium,
        Chest,
        GraveBody,
        GravePlace,
        Bed,
    }

    /// <summary>Summarized craft state of an object (without exposing the game's enum to the bot).</summary>
    internal enum CraftState
    {
        Unknown,
        Idle,
        Running,
        ReadyToCollect,
        Other,
    }

    /// <summary>Opaque handle to a world object (the bot never keeps game types).</summary>
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

    /// <summary>Opaque handle to an item on the ground (e.g. a body).</summary>
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
    /// Part 4: world queries (current scene), movement with the game's own pathfinding, and aiming.
    /// Only what the player could do: walk, turn around, look at what is on the ground/on the tables.
    /// </summary>
    internal static partial class GameApi
    {
        private const float PlayerWalkSpeed = 3.3f; // same speed the game uses when it moves the player by itself

        private static AccessTools.FieldRef<MovementComponent, MovementComponent.Status> movementStatusRef;
        private static bool moveRequested;

        // ------------------------------------------------------------------ internal helpers

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

        /// <summary>"Regular" body: excludes zombies and bodies with a demon (the MVP does not touch them).</summary>
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

        // ------------------------------------------------------------------ queries

        /// <summary>Regular bodies on the ground of the current scene, nearest first, up to <paramref name="maxDistance"/>.</summary>
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

        /// <summary>Objects of one kind in the current scene, nearest first.</summary>
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
                    return w.id == "grave_body"; // grave with a body, still to be closed (shovel work)
                case ObjectKind.Bed:
                    return w.CustomTag == "bed_home"; // the bed in the player's house (dump: id "bed", Script interaction)
                case ObjectKind.GravePlace:
                    return w.id == "grave_empty_place"; // grave marked by the builder, still to be dug (shovel → grave_empty)
                case ObjectKind.Chest:
                    return w.Definition.interactionType == WGODef.InteractionType.Chest
                        && w.Definition.inventorySize > 0
                        && w.Definition.conveyorType == ConveyorElementType.None // conveyor/garden/wine chests are something else
                        && string.IsNullOrEmpty(w.CustomTag)                    // quest chests (e.g. chest_resurrection) have a tag
                        && !w.id.StartsWith("garden_bags_storage");
                default:
                    return false;
            }
        }

        /// <summary>Current id of the object (changes when the grave becomes grave_body, for example). null if it is gone.</summary>
        public static string GetObjectDefId(string uid) => Safe(() => FindWgoByUid(uid)?.id, null, nameof(GetObjectDefId));

        public static bool ObjectExists(string uid) => Safe(() => FindWgoByUid(uid) != null, false, nameof(ObjectExists));

        public static bool GroundItemExists(string uid) => Safe(() => FindDropByUid(uid) != null, false, nameof(GroundItemExists));

        /// <summary>Does the object have a body inside (table, grave)?</summary>
        public static bool ObjectHasBody(string uid) => Safe(() =>
        {
            WgoData w = FindWgoByUid(uid);
            return w != null && FindBodyInInventory(w) != null;
        }, false, nameof(ObjectHasBody));

        /// <summary>Is the body inside the object a regular one (not zombie/demon)?</summary>
        public static bool ObjectHasPlainBody(string uid) => Safe(() =>
        {
            WgoData w = FindWgoByUid(uid);
            return w != null && IsPlainBody(FindBodyInInventory(w));
        }, false, nameof(ObjectHasPlainBody));

        /// <summary>Unique id of the body inside the object (to track the same body across steps).</summary>
        public static string GetBodyUidInObject(string uid) => Safe(() =>
        {
            WgoData w = FindWgoByUid(uid);
            return w == null ? null : FindBodyInInventory(w)?.UniqueId?.Id;
        }, null, nameof(GetBodyUidInObject));

        /// <summary>Craft in progress or queued (the player needs to work or wait)?</summary>
        public static bool IsCraftActive(string uid) => Safe(() =>
        {
            CraftComponent cc = FindWgoByUid(uid)?.CraftComponent;
            return cc != null && (cc.IsStarted || cc.HasCraftsInQueue);
        }, false, nameof(IsCraftActive));

        /// <summary>Item the autopsy craft in progress will yield (e.g. "skull_2_2:2"); null if it is not an organ extraction.</summary>
        public static string GetActiveAutopsyItemId(string uid) => Safe(() =>
        {
            CraftDef def = FindWgoByUid(uid)?.CraftComponent?.CurrentCraftElement?.Def as CraftDef;
            return def == null || string.IsNullOrEmpty(def.autopsyItemId) ? null : def.autopsyItemId;
        }, null, nameof(GetActiveAutopsyItemId));

        /// <summary>The object's craft state: idle, running, ready to collect (auto-craft finished).</summary>
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

        /// <summary>Progress of the current craft in ticks (to detect stalled work). -1 if there is none.</summary>
        public static int GetCraftProgressTicks(string uid) => Safe(() =>
        {
            CraftElementBase e = FindWgoByUid(uid)?.CraftComponent?.CurrentCraftElement;
            return e == null ? -1 : e.ProgressTicks;
        }, -1, nameof(GetCraftProgressTicks));

        /// <summary>Is another worker (zombie/NPC) assigned to the object? The player does not count.</summary>
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

        // ------------------------------------------------------------------ interaction spots

        /// <summary>
        /// Where the player should stand to use the object: the nearest free dock point (preferring the player ones,
        /// not the zombie ones). If the object has not been instantiated on screen yet, returns its position.
        /// </summary>
        private const float DockClearRadius = 0.2f;   // player radius used to check whether the work spot is free
        private static readonly HashSet<string> LoggedDockTargets = new HashSet<string>();
        /// <summary>Last work spot chosen per object: used when the object's view is gone (not spawned yet / removed).</summary>
        private static readonly Dictionary<string, KeyValuePair<Vector3, Vector2>> LastDockSpots = new Dictionary<string, KeyValuePair<Vector3, Vector2>>();
        private static readonly HashSet<string> LoggedDockFallbacks = new HashSet<string>();

        private const float DockCrowdDistance = 1.0f; // another large object closer than this to the spot = crowded

        /// <summary>Id of another interactive object (table, chest, pallet, crematorium…) right next to the spot, or null.</summary>
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
            try { return f(); } catch { return true; } // when in doubt, don't discard the spot
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
                    if (LastDockSpots.TryGetValue(uid, out KeyValuePair<Vector3, Vector2> cached))
                    {
                        s = cached.Key;
                        f = cached.Value;
                    }
                    return true;
                }
                Vector3 p = MainGame.PlayerData.position.Value;
                RecastGraph rg = PlayerNavGraph() as RecastGraph;
                DockPoint best = null;
                float bestScore = float.MaxValue;
                var diag = new System.Text.StringBuilder();
                // The game deactivates the whole view of an object that is off screen (chunk culling, Wgo.RefreshVisuals),
                // but its work spots are still where they were. Then only the spot's own flag counts: with
                // activeInHierarchy, an off-screen crematorium had no spot and the bot aimed at its centre, which is off
                // the player's navmesh ("no path found to the target"). On screen, keep the stricter check.
                bool culled = !view.gameObject.activeInHierarchy;
                foreach (DockPoint d in docks)
                {
                    if (d == null || !(culled ? d.gameObject.activeSelf : d.gameObject.activeInHierarchy) || d.DontUseForWorkerPlacement)
                    {
                        continue;
                    }
                    // Work spot the player cannot reach: another object on top of it (solid collider of another Wgo,
                    // e.g. a chest pushed against it) or off the navmesh. The bot walks a scripted path and would pass right over it.
                    bool free = SafeBool(() => d.IsReachable(DockClearRadius));
                    bool onMesh = rg == null || SafeBool(() => d.IsReachable(rg));
                    Vector3 dp = d.transform.position;
                    // Squeezed between objects (e.g. the gap between the two tables): the player can't get through, the scripted path can.
                    string crowd = CrowdingObject(dp, w.UniqueId.Id);
                    diag.Append($" [{dp.x:0.00},{dp.z:0.00} {d.Direction}{(d.IsForZombie ? Lang.T(" zumbi", " zombie") : "")}{(free ? "" : Lang.T(" BLOQUEADO", " BLOCKED"))}"
                        + $"{(crowd != null ? Lang.T(" APERTADO por ", " CROWDED by ") + crowd : "")}{(onMesh ? "" : Lang.T(" fora-navmesh", " off-navmesh"))}]");
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
                    ModLog.Detail(Lang.T($"Pontos de trabalho de {w.id}:{diag} → escolhido {bp.x:0.00},{bp.z:0.00}", $"Work spots for {w.id}:{diag} → chosen {bp.x:0.00},{bp.z:0.00}"));
                }
                if (best != null)
                {
                    s = best.transform.position;
                    f = best.Direction != Direction.None ? best.Direction.ConvertToVector2XZ() : Vector2.zero;
                    LastDockSpots[uid] = new KeyValuePair<Vector3, Vector2>(s, f);
                }
                else if (LastDockSpots.TryGetValue(uid, out KeyValuePair<Vector3, Vector2> last))
                {
                    s = last.Key;
                    f = last.Value;
                    if (LoggedDockFallbacks.Add(uid))
                    {
                        ModLog.Detail(Lang.T($"Pontos de trabalho de {w.id} indisponíveis agora — uso o último escolhido ({s.x:0.00},{s.z:0.00})",
                            $"Work spots for {w.id} unavailable right now — using the last one chosen ({s.x:0.00},{s.z:0.00})"));
                    }
                }
                return true;
            }, false, nameof(TryGetStandSpot));
            spot = s;
            facing = f;
            return ok;
        }

        /// <summary>A point just short of the item on the ground, coming from the player's side (so the player faces it).</summary>
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

        /// <summary>Turns the player to the given (x,z) direction, as the analog stick would.</summary>
        public static void FaceDirection(Vector2 dirXZ) => Safe(() =>
        {
            MainGame.PlayerController.PhysicalBody.SetFacingDirection(dirXZ);
            return true;
        }, false, nameof(FaceDirection));

        /// <summary>Turns the player toward a world point.</summary>
        public static void FaceTowards(Vector3 target) => Safe(() =>
        {
            Vector3 d = target - MainGame.PlayerData.position.Value;
            MainGame.PlayerController.PhysicalBody.SetFacingDirection(new Vector2(d.x, d.z));
            return true;
        }, false, nameof(FaceTowards));

        /// <summary>Is the object the current interaction target (what E would trigger)? Large items on the ground take priority in the game.</summary>
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
        /// Uid of the game's current interaction target (object or large item on the ground), or null if there is no target.
        /// Used to never hold Action while aiming at something else (Action on a chest = "take all").
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

        /// <summary>Is the item on the ground the current interaction target?</summary>
        public static bool IsGroundItemUnderInteraction(string uid) => Safe(() =>
        {
            DropView v = MainGame.PlayerController.PlayerInteractionComponent.BigDropUnderInteraction;
            return v != null && v.Data != null && v.Data.UniqueId.Id == uid;
        }, false, nameof(IsGroundItemUnderInteraction));

        /// <summary>Description of the current interaction target (for log/overlay).</summary>
        public static string DescribeInteractionTarget() => Safe(() =>
        {
            PlayerInteractionComponent pic = MainGame.PlayerController.PlayerInteractionComponent;
            if (pic.BigDropUnderInteraction?.Data != null)
            {
                return Lang.T("chão: ", "ground: ") + pic.BigDropUnderInteraction.Data.Id;
            }
            return pic.WgoUnderInteraction != null && pic.WgoUnderInteraction.HasData ? pic.WgoUnderInteraction.Data.id : "-";
        }, "?", nameof(DescribeInteractionTarget));

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// Walks to <paramref name="target"/> using the same pathfinding (the scene's Recast graph) the game uses
        /// to move the player in scripted scenes/fishing. Does not teleport.
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
                ModLog.Warn(Lang.T($"Movimento recusado pelo jogo: {r}", $"Movement refused by the game: {r}"));
                return false;
            }
            return true;
        }, false, nameof(StartMoveTo));

        /// <summary>State of the last movement requested by the bot.</summary>
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
            // Finished: success, or the game found no path (status goes back to None without Completion).
            return mc.Completion == MovementComponent.CompletionState.Success ? MoveState.Arrived : MoveState.Failed;
        }, MoveState.Failed, nameof(GetMoveState));

        /// <summary>Stops the movement started by the bot and gives the player back normal physics.</summary>
        public static void StopMoving() => Safe(() =>
        {
            if (!moveRequested)
            {
                return true;
            }
            moveRequested = false;
            MovementComponent mc = MainGame.PlayerController.MovementComponent;
            // ForceStop also calls the player's OnPathComplete (back to dynamic physics), including
            // when the game found no path and left the body in kinematic mode.
            mc.ForceStop();
            return true;
        }, false, nameof(StopMoving));

        /// <summary>Marks the movement as finished without ForceStop (arrived normally).</summary>
        public static void ClearMoveRequest()
        {
            moveRequested = false;
        }
    }
}

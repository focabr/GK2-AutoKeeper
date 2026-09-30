using System.Collections.Generic;
using System.Text.RegularExpressions;
using LazyBearTechnology;
using Pathfinding;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Door that teleports the player (the game's tp_* objects: CustomInteraction with TeleportTo("destination")).
    /// The bot uses the door the same way the player does: walks up to it and presses E.
    /// </summary>
    internal readonly struct DoorRef
    {
        public readonly string Uid;
        public readonly string Id;
        public readonly Vector3 Position;       // the door, on this side
        public readonly Vector3 Landing;        // where the player appears on the other side
        public readonly string DestinationId;   // door on the other side

        public DoorRef(string uid, string id, Vector3 position, Vector3 landing, string destinationId)
        {
            Uid = uid;
            Id = id;
            Position = position;
            Landing = landing;
            DestinationId = destinationId;
        }

        /// <summary>Short name for the panel: "tp_RT_home_exit" → "home exit".</summary>
        public string Label => Id != null && Id.StartsWith("tp_RT_") ? Id.Substring(6).Replace('_', ' ') : Id;

        public override string ToString() => $"{Id}#{WorldObjectRef.ShortUid(Uid)}";
    }

    /// <summary>
    /// Part 7: navigation between areas. The game's navmesh (the scene's Recast graph, the same one MovementComponent uses)
    /// has separate "islands": each interior (house, morgue…) is a region with no walking connection to the outside.
    /// The tp_* doors connect these regions. This only queries: which region contains a point and which doors exist.
    /// </summary>
    internal static partial class GameApi
    {
        private const float NavSnapDistance = 4f; // a point farther than this from the navmesh = "off the walkable map"

        private static readonly Regex TeleportToRegex = new Regex("TeleportTo\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>Recast graph the game uses to move the player in the current scene (MovementComponent.FindPathRecastGraph).</summary>
        private static NavGraph PlayerNavGraph()
        {
            AstarPath ap = AstarPath.active;
            NavGraph[] graphs = ap != null && ap.data != null ? ap.data.graphs : null;
            GraphHelper helper = GraphHelper.Instance;
            if (graphs == null || helper == null || helper.SceneGraphsData == null)
            {
                return null;
            }
            List<int> idx = helper.SceneGraphsData.GetRecastGraphIndexByWorldId(MainGame.PlayerData.currentGameSceneId);
            if (idx == null || idx.Count == 0 || idx[0] < 0 || idx[0] >= graphs.Length)
            {
                return null;
            }
            return graphs[idx[0]];
        }

        private static uint NavAreaCore(Vector3 pos, out Vector3 snapped)
        {
            snapped = pos;
            NavGraph g = PlayerNavGraph();
            if (g == null)
            {
                return 0;
            }
            NNInfo nn = g.GetNearest(pos, NearestNodeConstraint.Walkable);
            if (nn.node == null || (nn.position - pos).sqrMagnitude > NavSnapDistance * NavSnapDistance)
            {
                return 0;
            }
            snapped = nn.position;
            return nn.node.Area;
        }

        /// <summary>Walkable region (connected component of the navmesh) that contains the point. 0 = unknown/off the navmesh.</summary>
        public static uint GetNavArea(Vector3 pos) => Safe(() => NavAreaCore(pos, out _), 0u, nameof(GetNavArea));

        /// <summary>Walkable region the player is in right now.</summary>
        public static uint GetPlayerNavArea() => Safe(() => NavAreaCore(MainGame.PlayerData.position.Value, out _), 0u, nameof(GetPlayerNavArea));

        /// <summary>Nearest walkable point (the door itself is usually in the wall, off the navmesh).</summary>
        public static Vector3 GetNearestWalkablePoint(Vector3 pos) => Safe(() =>
        {
            NavAreaCore(pos, out Vector3 snapped);
            return snapped;
        }, pos, nameof(GetNearestWalkablePoint));

        /// <summary>
        /// Doors of the current scene that the player can use now (door condition evaluated by the game itself)
        /// and whose destination exists. Read-only.
        /// </summary>
        public static List<DoorRef> FindDoors() => Safe(() =>
        {
            var result = new List<DoorRef>();
            string scene = MainGame.PlayerData.currentGameSceneId;
            foreach (WgoData w in CurrentScene().wgoDataList)
            {
                if (w?.Definition == null || !w.IsInteractable || w.Definition.interactionType != WGODef.InteractionType.CustomInteraction)
                {
                    continue;
                }
                CustomInteraction ci = w.Definition.customInteraction;
                string destId = TeleportDestination(ci);
                if (destId == null || !SafeIsInteractable(ci, w))
                {
                    continue;
                }
                if (!MainGame.WorldData.TryGetWgoData(destId, out WgoData dest, out GameSceneData destScene)
                    || dest == null || destScene == null || destScene.id != scene)
                {
                    continue; // destination not built or in another scene
                }
                result.Add(new DoorRef(w.UniqueId.Id, w.id, w.Position, dest.GetTeleportPointPosition(), destId));
            }
            return result;
        }, new List<DoorRef>(), nameof(FindDoors));

        private static string TeleportDestination(CustomInteraction ci)
        {
            if (ci?.execution == null)
            {
                return null;
            }
            foreach (LazyExpression e in ci.execution)
            {
                string text = e?.ToString();
                if (string.IsNullOrEmpty(text) || text.Contains("TeleportToGD"))
                {
                    continue;
                }
                Match m = TeleportToRegex.Match(text);
                if (m.Success)
                {
                    return m.Groups[1].Value;
                }
            }
            return null;
        }

        private static bool SafeIsInteractable(CustomInteraction ci, WgoData w)
        {
            try
            {
                return ci.IsInteractable(w);
            }
            catch
            {
                return false; // a condition the game could not evaluate: don't risk it
            }
        }
    }
}

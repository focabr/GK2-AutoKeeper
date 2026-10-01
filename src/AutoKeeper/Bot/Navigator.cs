using System.Collections.Generic;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot
{
    /// <summary>How to reach a walkable region: approximate cost (m) and the first door on the path.</summary>
    internal readonly struct AreaRoute
    {
        public readonly uint Area;
        public readonly float Cost;        // meters walked + penalty per door
        public readonly int Doors;         // how many doors on the path
        public readonly bool HasDoor;
        public readonly DoorRef FirstDoor; // door to use now (valid if HasDoor)
        public readonly Vector3 Entry;     // where the region is entered (player position or exit of the last door)

        public AreaRoute(uint area, float cost, int doors, bool hasDoor, DoorRef firstDoor, Vector3 entry)
        {
            Area = area;
            Cost = cost;
            Doors = doors;
            HasDoor = hasDoor;
            FirstDoor = firstDoor;
            Entry = entry;
        }
    }

    /// <summary>
    /// Plans routes between regions using the game's doors (simple Dijkstra: doors = edges).
    /// Logic only; everything that touches the game goes through GameApi. Door data is cached for a few seconds.
    /// </summary>
    internal sealed class Navigator
    {
        private const float DoorPenalty = 15f;      // prefers walking a bit more over crossing doors needlessly
        private const float CacheSeconds = 20f;

        private sealed class DoorNode
        {
            public DoorRef Door;
            public uint Area;          // region on this side
            public uint LandingArea;   // region on the other side
        }

        private readonly List<DoorNode> doors = new List<DoorNode>();
        private readonly HashSet<string> brokenDoors = new HashSet<string>(); // doors that did not work in this session
        private readonly Dictionary<string, uint> areaByUid = new Dictionary<string, uint>();
        private readonly Dictionary<string, uint> standAreaByUid = new Dictionary<string, uint>();
        private readonly Dictionary<string, float> standAreaRetryAt = new Dictionary<string, float>();
        private const float StandAreaRetrySeconds = 10f;
        private float doorsCachedAt = -999f;
        private string cachedScene;
        private string loggedDoors;     // "scene:count" already written to the debug log

        /// <summary>Increases every time the bot is turned back on; tasks use it to know they must redo their initial checks.</summary>
        public int Generation { get; private set; }

        /// <summary>Forgets caches (e.g. when the bot is turned back on).</summary>
        public void Reset()
        {
            Generation++;
            doorsCachedAt = -999f;
            brokenDoors.Clear();
            areaByUid.Clear();
            standAreaByUid.Clear();
            standAreaRetryAt.Clear();
        }

        public void MarkDoorBroken(string doorUid)
        {
            if (doorUid != null && brokenDoors.Add(doorUid))
            {
                doorsCachedAt = -999f;
            }
        }

        /// <summary>Region of an object (cached by uid; game objects do not move).</summary>
        public uint AreaOf(string uid, Vector3 pos)
        {
            if (uid != null && areaByUid.TryGetValue(uid, out uint a))
            {
                return a;
            }
            a = GameApi.GetNavArea(pos);
            if (uid != null && a != 0)
            {
                areaByUid[uid] = a;
            }
            return a;
        }

        /// <summary>
        /// Region where the player stands to use the object (its work spot), or 0 if it has no known work spot.
        /// Some objects have their centre on an isolated navmesh island — the crematorium — so their own region is
        /// never reachable; only the morgue's "same room" shortcut found it. From the house (after sleeping) the
        /// crematorium was "not found" and the bot sat idle.
        /// </summary>
        public uint StandAreaOf(string uid, Vector3 centre)
        {
            if (uid == null)
            {
                return 0;
            }
            if (standAreaByUid.TryGetValue(uid, out uint a))
            {
                return a;
            }
            if (standAreaRetryAt.TryGetValue(uid, out float retry) && Time.unscaledTime < retry)
            {
                return 0;
            }
            a = 0;
            if (GameApi.TryGetStandSpot(uid, out Vector3 spot, out _) && Vector3.Distance(spot, centre) > 0.01f)
            {
                a = GameApi.GetNavArea(spot);
            }
            if (a != 0)
            {
                standAreaByUid[uid] = a;
            }
            else
            {
                standAreaRetryAt[uid] = Time.unscaledTime + StandAreaRetrySeconds; // no work spot known yet (view not spawned)
            }
            return a;
        }

        /// <summary>All regions reachable from the player, with the first door of each route.</summary>
        public Dictionary<uint, AreaRoute> ReachableAreas(bool allowDoors)
        {
            var result = new Dictionary<uint, AreaRoute>();
            Vector3 player = GameApi.GetPlayerPosition();
            uint here = GameApi.GetPlayerNavArea();
            if (here == 0)
            {
                return result; // off the navmesh (cutscene, transition): do not plan
            }
            result[here] = new AreaRoute(here, 0f, 0, false, default, player);
            if (!allowDoors)
            {
                return result;
            }
            RefreshDoors();

            // Dijkstra over the doors. dist = cost up to CROSSING the door.
            int n = doors.Count;
            var dist = new float[n];
            var hops = new int[n];
            var first = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = float.MaxValue;
                first[i] = -1;
                if (doors[i].Area == here)
                {
                    dist[i] = Vector3.Distance(player, doors[i].Door.Position) + DoorPenalty;
                    hops[i] = 1;
                    first[i] = i;
                }
            }
            while (true)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                {
                    if (!done[i] && dist[i] < float.MaxValue && (best < 0 || dist[i] < dist[best]))
                    {
                        best = i;
                    }
                }
                if (best < 0)
                {
                    break;
                }
                done[best] = true;
                DoorNode d = doors[best];
                uint arrived = d.LandingArea;
                if (arrived == 0)
                {
                    continue;
                }
                if (!result.TryGetValue(arrived, out AreaRoute known) || dist[best] < known.Cost)
                {
                    result[arrived] = new AreaRoute(arrived, dist[best], hops[best], true, doors[first[best]].Door, d.Door.Landing);
                }
                for (int j = 0; j < n; j++)
                {
                    if (done[j] || doors[j].Area != arrived)
                    {
                        continue;
                    }
                    float nd = dist[best] + Vector3.Distance(d.Door.Landing, doors[j].Door.Position) + DoorPenalty;
                    if (nd < dist[j])
                    {
                        dist[j] = nd;
                        hops[j] = hops[best] + 1;
                        first[j] = first[best];
                    }
                }
            }
            return result;
        }

        /// <summary>Estimated cost to go from the player to a point in an already-routed region.</summary>
        public static float CostTo(AreaRoute route, Vector3 target) => route.Cost + Vector3.Distance(route.Entry, target);

        private void RefreshDoors()
        {
            string scene = GameApi.GetSceneId();
            if (scene == cachedScene && Time.unscaledTime - doorsCachedAt < CacheSeconds)
            {
                return;
            }
            cachedScene = scene;
            doorsCachedAt = Time.unscaledTime;
            doors.Clear();
            foreach (DoorRef d in GameApi.FindDoors())
            {
                if (brokenDoors.Contains(d.Uid))
                {
                    continue;
                }
                uint area = AreaOf(d.Uid, d.Position);
                uint landing = GameApi.GetNavArea(d.Landing);
                if (area == 0 || landing == 0 || area == landing)
                {
                    continue; // door off the navmesh or that does not change region: useless for routes
                }
                doors.Add(new DoorNode { Door = d, Area = area, LandingArea = landing });
            }
            string key = $"{scene}:{doors.Count}";
            if (key != loggedDoors)   // the cache refreshes every few seconds: only logs when it changes
            {
                loggedDoors = key;
                ModLog.Debug(Lang.T($"Navegação: {doors.Count} portas úteis na cena {scene}", $"Navigation: {doors.Count} usable doors in scene {scene}"));
            }
        }

        /// <summary>Summary for the dump/log: doors and regions.</summary>
        public IEnumerable<string> DescribeDoors()
        {
            RefreshDoors();
            foreach (DoorNode d in doors)
            {
                yield return Lang.T($"{d.Door.Id}: área {d.Area} → {d.LandingArea} (destino {d.Door.DestinationId})",
                    $"{d.Door.Id}: area {d.Area} → {d.LandingArea} (destination {d.Door.DestinationId})");
            }
        }
    }
}

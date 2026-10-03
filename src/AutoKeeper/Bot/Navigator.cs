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
    /// Logic only; everything that touches the game goes through GameApi. The door LIST is cached for a few seconds,
    /// but region numbers are always read fresh: the game renumbers the navmesh regions (A* connected components,
    /// node.Area) while playing — seen in the F10 dumps of a single session: house 766 → 765, yard 98 → 99,
    /// crematorium 1012 → 1011 / 324 → 325. Cached numbers (0.2.3–0.3.30) went stale and broke the routes:
    /// bodies outside "with no path", "no reachable home bed", tables/crematorium "not found".
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
        private readonly List<DoorRef> doorRefs = new List<DoorRef>();        // the scene's usable doors (cached CacheSeconds)
        // Doors that just failed, with the time they are usable again. Never for the whole session: a walk that failed for a
        // passing reason (0.3.32: the game froze the player after the scalpel animation) banned the only door into the house.
        private readonly Dictionary<string, float> brokenDoors = new Dictionary<string, float>();
        private readonly Dictionary<string, Vector3> standSpotByUid = new Dictionary<string, Vector3>(); // positions never change
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
            standSpotByUid.Clear();
            standAreaRetryAt.Clear();
        }

        /// <summary>Leaves a door out of the routes for <paramref name="seconds"/>.</summary>
        public void MarkDoorBroken(string doorUid, float seconds)
        {
            if (doorUid != null)
            {
                brokenDoors[doorUid] = Time.unscaledTime + seconds;
                doorsCachedAt = -999f;
            }
        }

        /// <summary>Forgets the doors left out of the routes (before giving up for lack of a route). Returns how many there were.</summary>
        public int ForgiveBrokenDoors()
        {
            int n = 0;
            foreach (float until in brokenDoors.Values)
            {
                if (Time.unscaledTime < until)
                {
                    n++;
                }
            }
            brokenDoors.Clear();
            doorsCachedAt = -999f;
            return n;
        }

        private bool IsDoorBroken(string uid) => brokenDoors.TryGetValue(uid, out float until) && Time.unscaledTime < until;

        /// <summary>Region of an object, read now (region numbers change while playing: never cache them).</summary>
        public uint AreaOf(string uid, Vector3 pos) => GameApi.GetNavArea(pos);

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
            // The spot's POSITION is cached (objects do not move); its region number is read now.
            if (standSpotByUid.TryGetValue(uid, out Vector3 known))
            {
                return GameApi.GetNavArea(known);
            }
            if (standAreaRetryAt.TryGetValue(uid, out float retry) && Time.unscaledTime < retry)
            {
                return 0;
            }
            if (GameApi.TryGetStandSpot(uid, out Vector3 spot, out _) && Vector3.Distance(spot, centre) > 0.01f)
            {
                standSpotByUid[uid] = spot;
                return GameApi.GetNavArea(spot);
            }
            standAreaRetryAt[uid] = Time.unscaledTime + StandAreaRetrySeconds; // no work spot known yet (view not spawned)
            return 0;
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

        /// <summary>Door list cached for a few seconds (scanning the scene is costly); regions recomputed on every call.</summary>
        private void RefreshDoors()
        {
            string scene = GameApi.GetSceneId();
            if (scene != cachedScene || Time.unscaledTime - doorsCachedAt >= CacheSeconds)
            {
                cachedScene = scene;
                doorsCachedAt = Time.unscaledTime;
                doorRefs.Clear();
                doorRefs.AddRange(GameApi.FindDoors());
            }
            doors.Clear();
            foreach (DoorRef d in doorRefs)
            {
                if (IsDoorBroken(d.Uid))
                {
                    continue;
                }
                uint area = GameApi.GetNavArea(d.Position);
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

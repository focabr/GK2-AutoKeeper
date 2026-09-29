using System.Collections.Generic;
using AutoKeeper.Core;
using UnityEngine;

namespace AutoKeeper.Bot
{
    /// <summary>Como chegar numa região andável: custo aproximado (m) e a primeira porta do caminho.</summary>
    internal readonly struct AreaRoute
    {
        public readonly uint Area;
        public readonly float Cost;        // metros andados + penalidade por porta
        public readonly int Doors;         // quantas portas no caminho
        public readonly bool HasDoor;
        public readonly DoorRef FirstDoor; // porta a usar agora (válida se HasDoor)
        public readonly Vector3 Entry;     // onde se chega na região (posição do jogador ou saída da última porta)

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
    /// Planeja rotas entre regiões usando as portas do jogo (Dijkstra simples: portas = arestas).
    /// Só lógica; tudo que toca o jogo passa pela GameApi. Os dados das portas ficam em cache por alguns segundos.
    /// </summary>
    internal sealed class Navigator
    {
        private const float DoorPenalty = 15f;      // prefere andar um pouco mais a atravessar portas à toa
        private const float CacheSeconds = 20f;

        private sealed class DoorNode
        {
            public DoorRef Door;
            public uint Area;          // região do lado de cá
            public uint LandingArea;   // região do outro lado
        }

        private readonly List<DoorNode> doors = new List<DoorNode>();
        private readonly HashSet<string> brokenDoors = new HashSet<string>(); // portas que não funcionaram nesta sessão
        private readonly Dictionary<string, uint> areaByUid = new Dictionary<string, uint>();
        private float doorsCachedAt = -999f;
        private string cachedScene;

        /// <summary>Aumenta a cada religada do bot; tarefas usam para saber que devem refazer verificações iniciais.</summary>
        public int Generation { get; private set; }

        /// <summary>Esquece caches (ex.: ao religar o bot).</summary>
        public void Reset()
        {
            Generation++;
            doorsCachedAt = -999f;
            brokenDoors.Clear();
            areaByUid.Clear();
        }

        public void MarkDoorBroken(string doorUid)
        {
            if (doorUid != null && brokenDoors.Add(doorUid))
            {
                doorsCachedAt = -999f;
            }
        }

        /// <summary>Região de um objeto (cache por uid; objetos do jogo não mudam de lugar).</summary>
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

        /// <summary>Todas as regiões alcançáveis a partir do jogador, com a primeira porta de cada rota.</summary>
        public Dictionary<uint, AreaRoute> ReachableAreas(bool allowDoors)
        {
            var result = new Dictionary<uint, AreaRoute>();
            Vector3 player = GameApi.GetPlayerPosition();
            uint here = GameApi.GetPlayerNavArea();
            if (here == 0)
            {
                return result; // fora do navmesh (cutscene, transição): não planejar
            }
            result[here] = new AreaRoute(here, 0f, 0, false, default, player);
            if (!allowDoors)
            {
                return result;
            }
            RefreshDoors();

            // Dijkstra sobre as portas. dist = custo até ATRAVESSAR a porta.
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

        /// <summary>Custo estimado de ir do jogador até um ponto de uma região já roteada.</summary>
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
                    continue; // porta fora do navmesh ou que não muda de região: inútil para rotas
                }
                doors.Add(new DoorNode { Door = d, Area = area, LandingArea = landing });
            }
            ModLog.Debug($"Navegação: {doors.Count} portas úteis na cena {scene}");
        }

        /// <summary>Resumo para o dump/log: portas e regiões.</summary>
        public IEnumerable<string> DescribeDoors()
        {
            RefreshDoors();
            foreach (DoorNode d in doors)
            {
                yield return $"{d.Door.Id}: área {d.Area} → {d.LandingArea} (destino {d.Door.DestinationId})";
            }
        }
    }
}

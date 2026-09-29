using System.Collections.Generic;
using System.Text.RegularExpressions;
using LazyBearTechnology;
using Pathfinding;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Porta que teleporta o jogador (objetos tp_* do jogo: CustomInteraction com TeleportTo("destino")).
    /// O bot usa a porta do mesmo jeito que o jogador: anda até ela e aperta E.
    /// </summary>
    internal readonly struct DoorRef
    {
        public readonly string Uid;
        public readonly string Id;
        public readonly Vector3 Position;       // a porta, do lado de cá
        public readonly Vector3 Landing;        // onde o jogador aparece do outro lado
        public readonly string DestinationId;   // porta do outro lado

        public DoorRef(string uid, string id, Vector3 position, Vector3 landing, string destinationId)
        {
            Uid = uid;
            Id = id;
            Position = position;
            Landing = landing;
            DestinationId = destinationId;
        }

        /// <summary>Nome curto para o painel: "tp_RT_home_exit" → "home exit".</summary>
        public string Label => Id != null && Id.StartsWith("tp_RT_") ? Id.Substring(6).Replace('_', ' ') : Id;

        public override string ToString() => $"{Id}#{WorldObjectRef.ShortUid(Uid)}";
    }

    /// <summary>
    /// Parte 7: navegação entre áreas. O navmesh do jogo (grafo Recast da cena, o mesmo que o MovementComponent usa)
    /// tem "ilhas" separadas: cada interior (casa, necrotério…) é uma região sem ligação a pé com o lado de fora.
    /// As portas tp_* ligam essas regiões. Aqui só se consulta: qual região contém um ponto e quais portas existem.
    /// </summary>
    internal static partial class GameApi
    {
        private const float NavSnapDistance = 4f; // ponto a mais que isso do navmesh = "fora do mapa andável"

        private static readonly Regex TeleportToRegex = new Regex("TeleportTo\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>Grafo Recast que o jogo usa para mover o jogador na cena atual (MovementComponent.FindPathRecastGraph).</summary>
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

        /// <summary>Região andável (componente conexo do navmesh) que contém o ponto. 0 = desconhecida/fora do navmesh.</summary>
        public static uint GetNavArea(Vector3 pos) => Safe(() => NavAreaCore(pos, out _), 0u, nameof(GetNavArea));

        /// <summary>Região andável onde o jogador está agora.</summary>
        public static uint GetPlayerNavArea() => Safe(() => NavAreaCore(MainGame.PlayerData.position.Value, out _), 0u, nameof(GetPlayerNavArea));

        /// <summary>Ponto andável mais perto (a porta em si costuma ficar na parede, fora do navmesh).</summary>
        public static Vector3 GetNearestWalkablePoint(Vector3 pos) => Safe(() =>
        {
            NavAreaCore(pos, out Vector3 snapped);
            return snapped;
        }, pos, nameof(GetNearestWalkablePoint));

        /// <summary>
        /// Portas da cena atual que o jogador pode usar agora (condição da porta avaliada pelo próprio jogo)
        /// e cujo destino existe. Somente leitura.
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
                    continue; // destino não construído ou em outra cena
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
                return false; // condição que o jogo não conseguiu avaliar: não arriscar
            }
        }
    }
}

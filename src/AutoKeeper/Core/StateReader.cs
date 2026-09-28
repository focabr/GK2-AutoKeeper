using System.Collections.Generic;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>Foto do estado do jogo num instante — usada pelo overlay e pelas decisões do bot.</summary>
    internal sealed class GameSnapshot
    {
        public bool InGame;
        public string BlockReason;
        public string GameVersion;
        public string SceneId;
        public string ZoneId;
        public string ZoneName;
        public Vector3 Position;
        public float Energy;
        public float EnergyMax;
        public float Insanity;
        public float Money;
        public float TimeOfDay;
        public int Day;
        public List<string> Overhead = new List<string>();

        /// <summary>Hora aproximada (assume timeOfDay 0 = 00:00). A confirmar no jogo.</summary>
        public string ClockText
        {
            get
            {
                if (TimeOfDay < 0f)
                {
                    return "?";
                }
                int minutes = Mathf.FloorToInt(TimeOfDay * 24f * 60f) % (24 * 60);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }
    }

    /// <summary>Lê o estado através da GameApi (nunca toca classes do jogo diretamente).</summary>
    internal static class StateReader
    {
        public static GameSnapshot Read()
        {
            var s = new GameSnapshot
            {
                InGame = GameApi.IsInGame,
                GameVersion = GameApi.GetGameVersion(),
            };
            s.BlockReason = GameApi.GetBlockReason();
            if (!s.InGame)
            {
                return s;
            }
            s.SceneId = GameApi.GetSceneId();
            s.ZoneId = GameApi.GetZoneId();
            s.ZoneName = GameApi.GetZoneName();
            s.Position = GameApi.GetPlayerPosition();
            s.Energy = GameApi.GetEnergy();
            s.EnergyMax = GameApi.GetEnergyMax();
            s.Insanity = GameApi.GetPlayerRes("insanity");
            s.Money = GameApi.GetPlayerRes("money");
            s.TimeOfDay = GameApi.GetTimeOfDay();
            s.Day = GameApi.GetDay();
            s.Overhead = GameApi.GetOverheadItemIds();
            return s;
        }
    }
}

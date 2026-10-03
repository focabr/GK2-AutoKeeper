using System.Collections.Generic;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>Snapshot of the game state at one instant — used by the overlay and by the bot's decisions.</summary>
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
        public float DaysWithoutSleep = -1f;
        public bool LackOfSleep;

        /// <summary>Game clock "HH:MM" (assumes timeOfDay 0 = 00:00).</summary>
        public string ClockText => GameApi.FormatClock(TimeOfDay);
    }

    /// <summary>Reads the state through GameApi (never touches game classes directly).</summary>
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
            s.DaysWithoutSleep = GameApi.GetDaysWithoutSleep();
            s.LackOfSleep = GameApi.HasLackOfSleep();
            return s;
        }
    }
}

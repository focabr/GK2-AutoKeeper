using System.Collections.Generic;
using LazyBearTechnology;

namespace AutoKeeper.Core
{
    /// <summary>Hot bar item that restores energy (copied data; the bot never keeps game types).</summary>
    internal readonly struct HotBarFood
    {
        public readonly int Slot;          // 0..3 = keys 1..4
        public readonly string ItemId;
        public readonly float Energy;      // energy restored per use
        public readonly float Insanity;    // insanity added per use (> 0 = bad effect)
        public readonly int Count;

        public HotBarFood(int slot, string itemId, float energy, float insanity, int count)
        {
            Slot = slot;
            ItemId = itemId;
            Energy = energy;
            Insanity = insanity;
            Count = count;
        }

        public override string ToString() => Lang.T($"{ItemId} (tecla {Slot + 1}, +{Energy:0} energia, {Count} un.)", $"{ItemId} (key {Slot + 1}, +{Energy:0} energy, {Count} pcs)");
    }

    /// <summary>
    /// Part 6: food from the hot bar. Only reads what is pinned to keys 1–4 (PlayerData.pinnedItems);
    /// the eating is done by the game itself, when the bot "presses" the key (GameKey.UseHotBarItemN → TryUseHotBarItem).
    /// </summary>
    internal static partial class GameApi
    {
        /// <summary>Items pinned to the hot bar that restore energy when used.</summary>
        public static List<HotBarFood> GetHotBarFoods() => Safe(() =>
        {
            var result = new List<HotBarFood>();
            PlayerData pd = MainGame.PlayerData;
            string[] pinned = pd.pinnedItems;
            if (pinned == null)
            {
                return result;
            }
            for (int i = 0; i < pinned.Length && i < 4; i++)
            {
                string id = pinned[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                Item it = pd.inventory.GetItemById(id);
                if (it == null || it.IsEmpty || it.Definition == null || it.IsSeed || it.IsFertilizer)
                {
                    continue; // seed/fertilizer on the bar = planting, not eating
                }
                ItemDef def = it.Definition;
                if (!def.CanBeUsed)
                {
                    continue;
                }
                float energy = def.GetGameResOnUse("energy");
                if (energy <= 0f)
                {
                    continue;
                }
                result.Add(new HotBarFood(i, id, energy, def.GetGameResOnUse("insanity"), CountPlayerItem(id)));
            }
            return result;
        }, new List<HotBarFood>(), nameof(GetHotBarFoods));
    }
}

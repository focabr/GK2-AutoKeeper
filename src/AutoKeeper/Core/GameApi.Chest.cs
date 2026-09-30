using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Part 8: chest. Stores in the chest the items the bot collected (extraction, crematorium) when the inventory fills up.
    /// It is the same as what the player does in the chest window ("move items"): takes from the inventory and puts into the chest,
    /// only what the chest accepts (the game's filters/space). Nothing is created or deleted.
    /// </summary>
    internal static partial class GameApi
    {
        /// <summary>Player inventory item counts by id (includes bag contents; bodies are left out).</summary>
        public static Dictionary<string, int> SnapshotPlayerItems() => Safe(() =>
        {
            var counts = new Dictionary<string, int>();
            CountItems(MainGame.PlayerData.inventory.Data, counts);
            return counts;
        }, new Dictionary<string, int>(), nameof(SnapshotPlayerItems));

        private static void CountItems(Item container, Dictionary<string, int> counts)
        {
            if (container?.Inventory == null)
            {
                return;
            }
            foreach (Item it in container.Inventory)
            {
                if (it == null || it.IsEmpty || IsBodyItem(it))
                {
                    continue;
                }
                counts.TryGetValue(it.id, out int have);
                counts[it.id] = have + Math.Max(1, it.Count);
                if (it.IsBag)
                {
                    CountItems(it, counts);
                }
            }
        }

        /// <summary>Total units stored in the chest (for the watchdog: the bot never takes anything from a chest).</summary>
        public static int ChestItemTotal(string chestUid) => Safe(() =>
        {
            WgoData chest = FindWgoByUid(chestUid);
            if (chest?.Inventory == null)
            {
                return -1;
            }
            var counts = new Dictionary<string, int>();
            CountItems(chest.Inventory.Data, counts);
            int n = 0;
            foreach (int c in counts.Values)
            {
                n += c;
            }
            return n;
        }, -1, nameof(ChestItemTotal));

        /// <summary>Free slots in the player's inventory (same count as the game window).</summary>
        public static int PlayerFreeSlots() => Safe(() =>
        {
            Item inv = MainGame.PlayerData.inventory.Data;
            return Mathf.Max(0, inv.InventorySize - inv.InventoryFillSize);
        }, 99, nameof(PlayerFreeSlots));

        /// <summary>Free slots in the chest's inventory (-1 if it cannot be read).</summary>
        public static int ChestFreeSlots(string chestUid) => Safe(() =>
        {
            Item data = FindWgoByUid(chestUid)?.Inventory?.Data;
            return data == null ? -1 : Mathf.Max(0, data.InventorySize - data.InventoryFillSize);
        }, -1, nameof(ChestFreeSlots));

        /// <summary>Does the chest accept at least one unit of any of the items (space + the game's filters)?</summary>
        public static bool ChestCanTakeAny(string chestUid, IEnumerable<string> itemIds) => Safe(() =>
        {
            WgoData chest = FindWgoByUid(chestUid);
            if (chest?.Inventory == null)
            {
                return false;
            }
            foreach (string id in itemIds)
            {
                if (chest.Inventory.CanAddItemToInventory(id, 1))
                {
                    return true;
                }
            }
            return false;
        }, false, nameof(ChestCanTakeAny));

        /// <summary>Does the chest already hold any of these items? (marks the chest "for storing" that kind of thing)</summary>
        public static bool ChestHasAny(string chestUid, IEnumerable<string> itemIds) => Safe(() =>
        {
            WgoData chest = FindWgoByUid(chestUid);
            if (chest?.Inventory == null)
            {
                return false;
            }
            foreach (string id in itemIds)
            {
                if (chest.Inventory.Data.GetTotalCountInInventory(id) > 0)
                {
                    return true;
                }
            }
            return false;
        }, false, nameof(ChestHasAny));

        /// <summary>
        /// Moves up to <c>wanted[id]</c> units of each item to the chest (limited to what the player has and what the chest accepts).
        /// Returns what was moved. Being near the chest is required (like the chest window, which only opens on interaction).
        /// </summary>
        public static Dictionary<string, int> DepositToChest(string chestUid, Dictionary<string, int> wanted, out string reason)
        {
            string why = null;
            Dictionary<string, int> moved = Safe(() => DepositImpl(chestUid, wanted, out why), new Dictionary<string, int>(), nameof(DepositToChest));
            reason = why;
            return moved;
        }

        private static Dictionary<string, int> DepositImpl(string chestUid, Dictionary<string, int> wanted, out string reason)
        {
            var moved = new Dictionary<string, int>();
            WgoData chest = FindWgoByUid(chestUid);
            if (chest == null) { reason = Lang.T("baú não encontrado", "chest not found"); return moved; }
            if (!IsNear(chest, out reason)) { return moved; }
            Inventory player = MainGame.PlayerData.inventory;
            Inventory dest = chest.Inventory;
            if (dest == null) { reason = Lang.T("o baú não tem inventário", "the chest has no inventory"); return moved; }

            foreach (KeyValuePair<string, int> kv in wanted)
            {
                int have = player.Data.GetTotalCountInInventory(kv.Key);
                int n = Math.Min(kv.Value, have);
                // How much fits: start from the total and go down (the game only answers yes/no per amount).
                while (n > 0 && !dest.CanAddItemToInventory(kv.Key, n))
                {
                    n = n > 8 ? n - Math.Max(1, n / 4) : n - 1;
                }
                if (n <= 0)
                {
                    continue;
                }
                List<Item> removed = player.RemoveItemById(kv.Key, n);
                if (removed == null || removed.Count == 0)
                {
                    continue;
                }
                int count = 0;
                foreach (Item it in removed)
                {
                    count += Math.Max(1, it.Count);
                }
                if (dest.AddItemsToInventory(removed))
                {
                    moved[kv.Key] = count;
                }
                else
                {
                    player.AddItemsToInventory(removed); // did not fit: give it back, nothing is lost
                }
            }
            reason = moved.Count == 0 ? Lang.T("o baú não aceitou nenhum item", "the chest did not accept any item") : null;
            return moved;
        }
    }
}

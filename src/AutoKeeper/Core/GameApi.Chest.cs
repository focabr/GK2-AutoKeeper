using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Parte 8: baú. Guardar no baú os itens que o bot recolheu (extração, crematório) quando o inventário enche.
    /// É o mesmo que o jogador faz na janela do baú ("mover itens"): tira do inventário e coloca no baú,
    /// só o que o baú aceita (filtros/espaço do jogo). Nada é criado nem apagado.
    /// </summary>
    internal static partial class GameApi
    {
        /// <summary>Contagem de itens do inventário do jogador por id (inclui o conteúdo de bolsas; corpos ficam de fora).</summary>
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

        /// <summary>Total de unidades guardadas no baú (para o vigia: o bot nunca tira nada de baú).</summary>
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

        /// <summary>Espaços livres no inventário do jogador (mesma conta da janela do jogo).</summary>
        public static int PlayerFreeSlots() => Safe(() =>
        {
            Item inv = MainGame.PlayerData.inventory.Data;
            return Mathf.Max(0, inv.InventorySize - inv.InventoryFillSize);
        }, 99, nameof(PlayerFreeSlots));

        /// <summary>O baú aceita pelo menos uma unidade de algum dos itens (espaço + filtros do jogo)?</summary>
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

        /// <summary>O baú já guarda algum destes itens? (indica o baú "de guardar" desse tipo de coisa)</summary>
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
        /// Move para o baú até <c>wanted[id]</c> unidades de cada item (limitado ao que o jogador tem e ao que o baú aceita).
        /// Devolve o que foi movido. Perto do baú é obrigatório (igual à janela de baú, que só abre em interação).
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
            if (chest == null) { reason = "baú não encontrado"; return moved; }
            if (!IsNear(chest, out reason)) { return moved; }
            Inventory player = MainGame.PlayerData.inventory;
            Inventory dest = chest.Inventory;
            if (dest == null) { reason = "o baú não tem inventário"; return moved; }

            foreach (KeyValuePair<string, int> kv in wanted)
            {
                int have = player.Data.GetTotalCountInInventory(kv.Key);
                int n = Math.Min(kv.Value, have);
                // Quanto cabe: começa pelo total e diminui (o jogo só responde sim/não por quantidade).
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
                    player.AddItemsToInventory(removed); // não coube: devolve, nada se perde
                }
            }
            reason = moved.Count == 0 ? "o baú não aceitou nenhum item" : null;
            return moved;
        }
    }
}

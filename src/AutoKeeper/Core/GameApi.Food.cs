using System.Collections.Generic;
using LazyBearTechnology;

namespace AutoKeeper.Core
{
    /// <summary>Item da barra rápida que recupera energia (dados copiados; o bot nunca guarda tipos do jogo).</summary>
    internal readonly struct HotBarFood
    {
        public readonly int Slot;          // 0..3 = teclas 1..4
        public readonly string ItemId;
        public readonly float Energy;      // energia recuperada por uso
        public readonly float Insanity;    // insanidade somada por uso (> 0 = efeito ruim)
        public readonly int Count;

        public HotBarFood(int slot, string itemId, float energy, float insanity, int count)
        {
            Slot = slot;
            ItemId = itemId;
            Energy = energy;
            Insanity = insanity;
            Count = count;
        }

        public override string ToString() => $"{ItemId} (tecla {Slot + 1}, +{Energy:0} energia, {Count} un.)";
    }

    /// <summary>
    /// Parte 6: comida da barra rápida. Só lê o que está fixado nas teclas 1–4 (PlayerData.pinnedItems);
    /// quem come é o próprio jogo, quando o bot "aperta" a tecla (GameKey.UseHotBarItemN → TryUseHotBarItem).
    /// </summary>
    internal static partial class GameApi
    {
        /// <summary>Itens fixados na barra rápida que recuperam energia ao usar.</summary>
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
                    continue; // semente/adubo na barra = plantar, não comer
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

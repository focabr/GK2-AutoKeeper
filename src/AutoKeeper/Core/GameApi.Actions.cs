using System;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>Maestria atual x exigida para uma extração (dados copiados).</summary>
    internal readonly struct ExtractMastery
    {
        public readonly int Mastery;
        public readonly int Required;

        public ExtractMastery(int mastery, int required)
        {
            Mastery = mastery;
            Required = required;
        }

        /// <summary>Chance (%) de cada golpe avançar, como a janela do jogo mostra. 0 = não dá para extrair.</summary>
        public int ChancePercent => Required <= 0 ? (Mastery > 0 ? 100 : 0) : System.Math.Min(100, (int)(100f * Mastery / Required));

        public override string ToString() => $"maestria {Mastery}/{Required} ({ChancePercent}%)";
    }

    /// <summary>
    /// Parte 5: ações equivalentes a cliques de UI. Usam as MESMAS classes de dados das janelas do jogo
    /// (UICraftSelectionWindowData / UISingleCraftWindowData) sem abrir a janela, e repetem exatamente o que
    /// o botão faz. Só funcionam com o jogador perto do objeto (como na UI) e nunca criam itens.
    /// </summary>
    internal static partial class GameApi
    {
        private const float MaxUseDistance = 3.5f;

        private static bool IsNear(WgoData w, out string reason)
        {
            float d = Vector3.Distance(MainGame.PlayerData.position.Value, w.Position);
            if (d > MaxUseDistance)
            {
                reason = $"longe demais do objeto ({d:0.0} m)";
                return false;
            }
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ autópsia

        /// <summary>
        /// Órgãos do corpo na mesa que ainda podem ser extraídos, filtrados pela config.
        /// <paramref name="allowAll"/> = todos; senão, aceita por tipo (Bones, Brain, Heart, Guts, Skin, Skull) ou por id de item.
        /// </summary>
        public static List<string> GetExtractableOrgans(string tableUid, bool allowAll, ICollection<string> allowed) => Safe(() =>
        {
            var result = new List<string>();
            WgoData table = FindWgoByUid(tableUid);
            Item body = table == null ? null : FindBodyInInventory(table);
            if (!IsPlainBody(body) || body.Inventory == null)
            {
                return result;
            }
            foreach (Item organ in body.Inventory)
            {
                if (organ == null || organ.IsEmpty || organ.Definition == null || organ.Definition.isOrganMistake)
                {
                    continue;
                }
                if (!LazyConsts.MAIN_ORGANS_TYPES.Contains(organ.Definition.type))
                {
                    continue; // itens de bolso não entram no MVP
                }
                bool wanted = allowAll || allowed.Contains(organ.Definition.type.ToString()) || allowed.Contains(organ.id);
                if (wanted && GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.ExtractOrgan, organ.id) != null)
                {
                    result.Add(organ.id);
                }
            }
            return result;
        }, new List<string>(), nameof(GetExtractableOrgans));

        /// <summary>
        /// Tipo de um item "Outros" (bolso) do corpo, como a janela de autópsia mostra: Flesh (carne), Fat (gordura),
        /// Blood (sangue) ou Other. Órgãos principais e certificados (burial_reward) não são itens de bolso.
        /// </summary>
        private static string PocketKind(Item item)
        {
            List<string> g = item.Definition.itemGroupIds ?? new List<string>();
            string id = item.id ?? "";
            if (g.Contains("gr_flesh") || item.Definition.type == ItemType.Flesh || id.StartsWith("flesh"))
            {
                return "Flesh";
            }
            if (g.Contains("gr_fat") || id.StartsWith("fat")) // GameConsts.FAT_ITEM_GROUP
            {
                return "Fat";
            }
            if (g.Contains("gr_blood") || id.StartsWith("blood"))
            {
                return "Blood";
            }
            return "Other";
        }

        private static bool IsPocketItem(Item item)
        {
            return item != null && !item.IsEmpty && item.Definition != null && !item.Definition.isMainOrgan
                && (item.Definition.itemGroupIds == null || !item.Definition.itemGroupIds.Contains("burial_reward"));
        }

        /// <summary>Itens da seção "Outros" da janela de autópsia (carne, gordura, sangue…) permitidos pela config.</summary>
        public static List<string> GetExtractablePocketItems(string tableUid, ICollection<string> allowedKinds) => Safe(() =>
        {
            var result = new List<string>();
            WgoData table = FindWgoByUid(tableUid);
            Item body = table == null ? null : FindBodyInInventory(table);
            if (!IsPlainBody(body) || body.Inventory == null || allowedKinds.Count == 0
                || GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.PocketExtract) == null)
            {
                return result;
            }
            foreach (Item it in body.Inventory)
            {
                if (IsPocketItem(it) && allowedKinds.Contains(PocketKind(it)) && !result.Contains(it.id))
                {
                    result.Add(it.id);
                }
            }
            return result;
        }, new List<string>(), nameof(GetExtractablePocketItems));

        /// <summary>
        /// = clicar num item de "Outros" na janela de autópsia e confirmar "extrair" (mesma sequência de
        /// UIAutopsyWindowData.TryExtractItemFromPocket). Tira 1 unidade; o item sai no fim do trabalho.
        /// </summary>
        public static bool StartPocketExtract(string tableUid, string itemId, out string reason)
        {
            string why = null;
            bool ok = Safe(() => StartPocketExtractImpl(tableUid, itemId, out why), false, nameof(StartPocketExtract));
            reason = ok ? null : (why ?? "erro interno (veja o log)");
            return ok;
        }

        private static bool StartPocketExtractImpl(string tableUid, string itemId, out string reason)
        {
            WgoData table = FindWgoByUid(tableUid);
            if (table == null) { reason = "mesa não encontrada"; return false; }
            if (!IsNear(table, out reason)) { return false; }
            if (table.CraftComponent.IsStarted || table.CraftComponent.HasCraftsInQueue) { reason = "mesa já tem receita em andamento"; return false; }
            Item body = FindBodyInInventory(table);
            if (!IsPlainBody(body)) { reason = "sem corpo comum na mesa"; return false; }
            Item pocket = body.Inventory?.Find(i => IsPocketItem(i) && i.id == itemId);
            if (pocket == null) { reason = $"{itemId} não está mais no corpo"; return false; }
            CraftDef def = GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.PocketExtract);
            if (def == null) { reason = "o jogo não tem receita de extrair item do bolso"; return false; }

            // A janela mostra cada unidade como uma célula (cópia com Count = 1) e passa essa célula para a receita.
            Item unit = pocket.Count == 1 ? pocket : Item.Copy(pocket);
            unit.Count = 1;
            PlayerController pc = MainGame.PlayerController;
            bool setWorker = false;
            if (table.Worker == null)
            {
                table.TrySetWorker(pc); // a janela de autópsia aberta faz o mesmo
                setWorker = true;
            }
            try
            {
                table.Inventory.RemoveItemsFromNestedItemById(def.destinationItemEnd, new List<NeedItemData> { new NeedItemData(pocket.id, 1) });
                var ce = new CraftElement(def.id, 1, new List<NeedItemData>(), new CraftParamsData(def.id, new GameRes()));
                ce.SetCustomItems(new List<Item> { unit });
                table.CraftComponent.AddToQueue(ce, addToQueueTop: true);
                table.CraftComponent.TryContinueFromQueue();
            }
            finally
            {
                if (setWorker)
                {
                    table.ClearWorker(); // igual ao fechar a janela
                }
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// Maestria do jogador para extrair um item na mesa, igual à janela "Remover …": maestria atual (talento + ferramentas
        /// + perks) contra a exigida pela receita (talentLock). Chance = % de cada golpe avançar (100 = maestria suficiente).
        /// </summary>
        public static ExtractMastery GetExtractMastery(string tableUid, string itemId, bool pocket) => Safe(() =>
        {
            WgoData table = FindWgoByUid(tableUid);
            CraftDef def = pocket
                ? GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.PocketExtract)
                : GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.ExtractOrgan, itemId);
            if (table == null || def == null)
            {
                return new ExtractMastery(0, 0);
            }
            int mastery = MainGame.PlayerController.GetMasteryLevelForTalentBranch(table.Definition.talent, def);
            return new ExtractMastery(mastery, def.talentLock);
        }, new ExtractMastery(0, 0), nameof(GetExtractMastery));

        /// <summary>= clicar no órgão na janela de autópsia e depois em "iniciar" (itens padrão da janela).</summary>
        public static bool StartAutopsyExtract(string tableUid, string organItemId, out string reason)
        {
            string why = null;
            bool ok = Safe(() => StartAutopsyExtractImpl(tableUid, organItemId, out why), false, nameof(StartAutopsyExtract));
            reason = ok ? null : (why ?? "erro interno (veja o log)");
            return ok;
        }

        private static bool StartAutopsyExtractImpl(string tableUid, string organItemId, out string reason)
        {
            WgoData table = FindWgoByUid(tableUid);
            if (table == null) { reason = "mesa não encontrada"; return false; }
            if (!IsNear(table, out reason)) { return false; }
            if (table.CraftComponent.IsStarted || table.CraftComponent.HasCraftsInQueue) { reason = "mesa já tem receita em andamento"; return false; }
            Item body = FindBodyInInventory(table);
            if (!IsPlainBody(body)) { reason = "sem corpo comum na mesa"; return false; }

            Item organ = body.Inventory?.Find(i => i != null && i.id == organItemId);
            if (organ == null) { reason = $"órgão {organItemId} não está mais no corpo"; return false; }
            CraftDef def = GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.ExtractOrgan, organ.id);
            if (def == null) { reason = $"sem receita de extração para {organ.id}"; return false; }

            PlayerController pc = MainGame.PlayerController;
            bool setWorker = false;
            if (table.Worker == null)
            {
                table.TrySetWorker(pc);   // a janela faz o mesmo enquanto está aberta
                setWorker = true;
            }
            try
            {
                bool started = false;
                var data = new UICraftSelectionWindowData(table, def, null, (cd, needs, prm, count) =>
                {
                    var ce = new CraftElement(def.id, count, needs, prm);
                    table.CraftComponent.AddToQueue(ce, addToQueueTop: true);
                    table.CraftComponent.TryContinueFromQueue();
                    started = true;
                });
                if (!data.CanStartCraft)
                {
                    reason = $"requisitos de {def.id} não atendidos (itens/ferramenta/talento?)";
                    return false;
                }
                data.OnCraftStarted();
                reason = started ? null : "o jogo não aceitou a receita";
                return started;
            }
            finally
            {
                if (setWorker)
                {
                    table.ClearWorker(); // igual ao fechar a janela
                }
            }
        }

        /// <summary>= botão "tirar corpo" da janela de autópsia: o corpo vai para cima da cabeça do jogador.</summary>
        public static bool TakeBodyFromTable(string tableUid, out string reason)
        {
            string why = null;
            bool ok = Safe(() => TakeBodyFromTableImpl(tableUid, out why), false, nameof(TakeBodyFromTable));
            reason = ok ? null : (why ?? "erro interno (veja o log)");
            return ok;
        }

        private static bool TakeBodyFromTableImpl(string tableUid, out string reason)
        {
            WgoData table = FindWgoByUid(tableUid);
            if (table == null) { reason = "mesa não encontrada"; return false; }
            if (!IsNear(table, out reason)) { return false; }
            if (table.CraftComponent.IsStarted || table.CraftComponent.HasCraftsInQueue) { reason = "mesa ainda tem receita em andamento"; return false; }
            Item body = FindBodyInInventory(table);
            if (!IsPlainBody(body)) { reason = "sem corpo comum na mesa"; return false; }
            PlayerData pd = MainGame.PlayerData;
            if (pd.HasOverheadItem || !pd.HasFreeOverheadSlot) { reason = "o jogador já está carregando algo"; return false; }

            // Mesma sequência de UIAutopsyWindowData.TakeBody.
            pd.AddOverheadItem(body);
            table.Inventory.RemoveItemFromInventoryByUID(body);
            GameScene.GetWgoViewGlobal(table.UniqueId)?.DrawWidgets();
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ cova
        // Enterrar não é receita: com um corpo na cabeça, apertar E em "grave_empty" executa
        // InsertOvrhdItem() + ChangeWgo("grave_body"); depois "grave_body" é um trabalho com pá (segurar Ação).
        // O bot faz os dois como o jogador (tecla E e Ação segurada) — ver ProcessBodiesTask.

        /// <summary>Ids das receitas disponíveis no objeto (diagnóstico).</summary>
        public static List<string> GetCraftIds(string uid) => Safe(() =>
        {
            var ids = new List<string>();
            WgoData w = FindWgoByUid(uid);
            if (w?.CraftComponent != null && w.CraftComponent.HasCraftsByBalance)
            {
                foreach (CraftDefBase c in w.CraftComponent.CraftsIn)
                {
                    ids.Add(c.id);
                }
            }
            return ids;
        }, new List<string>(), nameof(GetCraftIds));
    }
}

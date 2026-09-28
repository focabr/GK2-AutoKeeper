using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutoKeeper.Core
{
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

        /// <summary>
        /// = abrir a receita da cova vazia e clicar "iniciar". A receita é a que exige um corpo
        /// (ou a definida em <paramref name="craftIdOverride"/>). O corpo carregado é consumido pela receita.
        /// </summary>
        public static bool StartBurial(string graveUid, string craftIdOverride, out string reason)
        {
            string why = null;
            bool ok = Safe(() => StartBurialImpl(graveUid, craftIdOverride, out why), false, nameof(StartBurial));
            reason = ok ? null : (why ?? "erro interno (veja o log)");
            return ok;
        }

        private static bool StartBurialImpl(string graveUid, string craftIdOverride, out string reason)
        {
            WgoData grave = FindWgoByUid(graveUid);
            if (grave == null) { reason = "cova não encontrada"; return false; }
            if (grave.id != "grave_empty") { reason = $"objeto não é uma cova vazia ({grave.id})"; return false; }
            if (!IsNear(grave, out reason)) { return false; }
            if (grave.CraftComponent.IsStarted || grave.CraftComponent.HasCraftsInQueue) { reason = "cova já tem receita em andamento"; return false; }
            if (!IsPlainBody(CarriedBody())) { reason = "o jogador não está carregando um corpo comum"; return false; }

            CraftDef def = PickBurialCraft(grave, craftIdOverride, out reason);
            if (def == null) { return false; }

            PlayerController pc = MainGame.PlayerController;
            CraftComponent cc = grave.CraftComponent;
            bool setWorker = false;
            if (grave.Worker == null)
            {
                grave.TrySetWorker(pc);
                setWorker = true;
            }
            try
            {
                bool started = false;
                // Mesmo caminho de CraftInteractionHandler.FormCraftElementAndStartCraft/OnCraftPressed.
                var data = new UISingleCraftWindowData(grave, def, null, (cd, needs, prm, count) =>
                {
                    var ce = new CraftElement(def.id, count, needs, prm);
                    ce.DoBeforeStartCalculations(grave);
                    if (ce.Definition.skipQueue)
                    {
                        if (cc.GetStartCraftStatus(ce) == CraftStatus.OK && ce.CanFinishCraft(grave) == CraftStatus.OK)
                        {
                            cc.ProcessInstantCraft(grave, ce);
                            started = true;
                        }
                        return;
                    }
                    cc.AddToQueue(ce, addToQueueTop: true);
                    cc.TryContinueFromQueue();
                    started = true;
                });
                if (!data.CanStartCraft)
                {
                    reason = $"requisitos de {def.id} não atendidos (pá? itens?)";
                    return false;
                }
                data.OnCraftStarted();
                reason = started ? null : "o jogo não aceitou a receita de enterro";
                return started;
            }
            finally
            {
                if (setWorker)
                {
                    grave.ClearWorker();
                }
            }
        }

        private static CraftDef PickBurialCraft(WgoData grave, string craftIdOverride, out string reason)
        {
            reason = null;
            if (!string.IsNullOrEmpty(craftIdOverride))
            {
                CraftDef forced = GameBalance.Me.GetDataOrNull<CraftDef>(craftIdOverride);
                if (forced == null)
                {
                    reason = $"GraveCraftId '{craftIdOverride}' não existe";
                }
                return forced;
            }
            var ids = new List<string>();
            foreach (CraftDefBase c in grave.CraftComponent.CraftsIn)
            {
                ids.Add(c.id);
                if (c is CraftDef cd && NeedsBody(cd))
                {
                    return cd;
                }
            }
            reason = $"receita de enterro não identificada em grave_empty (receitas: {string.Join(", ", ids)}). Defina [Bodies] GraveCraftId.";
            ModLog.WarnOnce("burial-craft", reason);
            return null;
        }

        private static bool NeedsBody(CraftDef cd)
        {
            foreach (NeedItemData n in cd.needItems)
            {
                if (n == null)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(n.id) && n.id.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(n.id);
                if (def?.itemGroupIds != null && def.itemGroupIds.Contains("body"))
                {
                    return true;
                }
            }
            return false;
        }

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

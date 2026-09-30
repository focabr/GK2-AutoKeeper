using System;
using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>Current vs. required mastery for an extraction (copied data).</summary>
    internal readonly struct ExtractMastery
    {
        public readonly int Mastery;
        public readonly int Required;

        public ExtractMastery(int mastery, int required)
        {
            Mastery = mastery;
            Required = required;
        }

        /// <summary>Chance (%) that each hit makes progress, as the game window shows. 0 = cannot extract.</summary>
        public int ChancePercent => Required <= 0 ? (Mastery > 0 ? 100 : 0) : System.Math.Min(100, (int)(100f * Mastery / Required));

        public override string ToString() => Lang.T($"chance de sucesso {ChancePercent}% (maestria {Mastery}/{Required})", $"success chance {ChancePercent}% (mastery {Mastery}/{Required})");
    }

    /// <summary>
    /// Part 5: actions equivalent to UI clicks. They use the SAME data classes as the game's windows
    /// (UICraftSelectionWindowData / UISingleCraftWindowData) without opening the window, and repeat exactly what
    /// the button does. They only work with the player near the object (as in the UI) and never create items.
    /// </summary>
    internal static partial class GameApi
    {
        private const float MaxUseDistance = 3.5f;

        private static bool IsNear(WgoData w, out string reason)
        {
            float d = Vector3.Distance(MainGame.PlayerData.position.Value, w.Position);
            if (d > MaxUseDistance)
            {
                reason = Lang.T($"longe demais do objeto ({d:0.0} m)", $"too far from the object ({d:0.0} m)");
                return false;
            }
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ autopsy

        /// <summary>
        /// Organs of the body on the table that can still be extracted, filtered by the config.
        /// <paramref name="allowAll"/> = all; otherwise, accepts by type (Bones, Brain, Heart, Guts, Skin, Skull) or by item id.
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
                    continue; // pocket items are not part of the MVP
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
        /// Kind of an "Others" (pocket) item of the body, as the autopsy window shows: Flesh, Fat,
        /// Blood or Other. Main organs and certificates (burial_reward) are not pocket items.
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

        /// <summary>Items in the "Others" section of the autopsy window (flesh, fat, blood…) allowed by the config.</summary>
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
        /// = clicking an "Others" item in the autopsy window and confirming "extract" (same sequence as
        /// UIAutopsyWindowData.TryExtractItemFromPocket). Takes 1 unit; the item comes out at the end of the work.
        /// </summary>
        public static bool StartPocketExtract(string tableUid, string itemId, out string reason)
        {
            string why = null;
            bool ok = Safe(() => StartPocketExtractImpl(tableUid, itemId, out why), false, nameof(StartPocketExtract));
            reason = ok ? null : (why ?? Lang.T("erro interno (veja o log)", "internal error (see the log)"));
            return ok;
        }

        private static bool StartPocketExtractImpl(string tableUid, string itemId, out string reason)
        {
            WgoData table = FindWgoByUid(tableUid);
            if (table == null) { reason = Lang.T("mesa não encontrada", "table not found"); return false; }
            if (!IsNear(table, out reason)) { return false; }
            if (table.CraftComponent.IsStarted || table.CraftComponent.HasCraftsInQueue) { reason = Lang.T("mesa já tem receita em andamento", "the table already has a craft in progress"); return false; }
            Item body = FindBodyInInventory(table);
            if (!IsPlainBody(body)) { reason = Lang.T("sem corpo comum na mesa", "no regular body on the table"); return false; }
            Item pocket = body.Inventory?.Find(i => IsPocketItem(i) && i.id == itemId);
            if (pocket == null) { reason = Lang.T($"{itemId} não está mais no corpo", $"{itemId} is no longer in the body"); return false; }
            CraftDef def = GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.PocketExtract);
            if (def == null) { reason = Lang.T("o jogo não tem receita de extrair item do bolso", "the game has no craft to extract pocket items"); return false; }

            // The window shows each unit as a cell (copy with Count = 1) and passes that cell to the craft.
            Item unit = pocket.Count == 1 ? pocket : Item.Copy(pocket);
            unit.Count = 1;
            PlayerController pc = MainGame.PlayerController;
            bool setWorker = false;
            if (table.Worker == null)
            {
                table.TrySetWorker(pc); // the open autopsy window does the same
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
                    table.ClearWorker(); // same as closing the window
                }
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// Player's mastery to extract an item at the table, same as the "Remove …" window: current mastery (talent + tools
        /// + perks) against the one required by the craft (talentLock). Chance = % that each hit makes progress (100 = enough mastery).
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

        /// <summary>= clicking the organ in the autopsy window and then "start" (the window's default items).</summary>
        public static bool StartAutopsyExtract(string tableUid, string organItemId, out string reason)
        {
            string why = null;
            bool ok = Safe(() => StartAutopsyExtractImpl(tableUid, organItemId, out why), false, nameof(StartAutopsyExtract));
            reason = ok ? null : (why ?? Lang.T("erro interno (veja o log)", "internal error (see the log)"));
            return ok;
        }

        private static bool StartAutopsyExtractImpl(string tableUid, string organItemId, out string reason)
        {
            WgoData table = FindWgoByUid(tableUid);
            if (table == null) { reason = Lang.T("mesa não encontrada", "table not found"); return false; }
            if (!IsNear(table, out reason)) { return false; }
            if (table.CraftComponent.IsStarted || table.CraftComponent.HasCraftsInQueue) { reason = Lang.T("mesa já tem receita em andamento", "the table already has a craft in progress"); return false; }
            Item body = FindBodyInInventory(table);
            if (!IsPlainBody(body)) { reason = Lang.T("sem corpo comum na mesa", "no regular body on the table"); return false; }

            Item organ = body.Inventory?.Find(i => i != null && i.id == organItemId);
            if (organ == null) { reason = Lang.T($"órgão {organItemId} não está mais no corpo", $"organ {organItemId} is no longer in the body"); return false; }
            CraftDef def = GameBalance.GetAutopsyCraftDef(AutopsyTypeCraft.ExtractOrgan, organ.id);
            if (def == null) { reason = Lang.T($"sem receita de extração para {organ.id}", $"no extraction craft for {organ.id}"); return false; }

            PlayerController pc = MainGame.PlayerController;
            bool setWorker = false;
            if (table.Worker == null)
            {
                table.TrySetWorker(pc);   // the window does the same while it is open
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
                    reason = Lang.T($"requisitos de {def.id} não atendidos (itens/ferramenta/talento?)", $"requirements for {def.id} not met (items/tool/talent?)");
                    return false;
                }
                data.OnCraftStarted();
                reason = started ? null : Lang.T("o jogo não aceitou a receita", "the game did not accept the craft");
                return started;
            }
            finally
            {
                if (setWorker)
                {
                    table.ClearWorker(); // same as closing the window
                }
            }
        }

        /// <summary>= the autopsy window's "take body" button: the body goes over the player's head.</summary>
        public static bool TakeBodyFromTable(string tableUid, out string reason)
        {
            string why = null;
            bool ok = Safe(() => TakeBodyFromTableImpl(tableUid, out why), false, nameof(TakeBodyFromTable));
            reason = ok ? null : (why ?? Lang.T("erro interno (veja o log)", "internal error (see the log)"));
            return ok;
        }

        private static bool TakeBodyFromTableImpl(string tableUid, out string reason)
        {
            WgoData table = FindWgoByUid(tableUid);
            if (table == null) { reason = Lang.T("mesa não encontrada", "table not found"); return false; }
            if (!IsNear(table, out reason)) { return false; }
            if (table.CraftComponent.IsStarted || table.CraftComponent.HasCraftsInQueue) { reason = Lang.T("mesa ainda tem receita em andamento", "the table still has a craft in progress"); return false; }
            Item body = FindBodyInInventory(table);
            if (!IsPlainBody(body)) { reason = Lang.T("sem corpo comum na mesa", "no regular body on the table"); return false; }
            PlayerData pd = MainGame.PlayerData;
            if (pd.HasOverheadItem || !pd.HasFreeOverheadSlot) { reason = Lang.T("o jogador já está carregando algo", "the player is already carrying something"); return false; }

            // Same sequence as UIAutopsyWindowData.TakeBody.
            pd.AddOverheadItem(body);
            table.Inventory.RemoveItemFromInventoryByUID(body);
            GameScene.GetWgoViewGlobal(table.UniqueId)?.DrawWidgets();
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ grave
        // Burying is not a craft: with a body overhead, pressing E on "grave_empty" runs
        // InsertOvrhdItem() + ChangeWgo("grave_body"); then "grave_body" is shovel work (hold Action).
        // The bot does both like the player (E key and Action held) — see ProcessBodiesTask.

        /// <summary>Ids of the crafts available on the object (diagnostics).</summary>
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

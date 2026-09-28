using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AutoKeeper.Core
{
    /// <summary>
    /// Parte 2: ferramenta de descoberta (Etapa 4). Gera um JSON SOMENTE LEITURA com os objetos (WGO),
    /// itens no chão e receitas relevantes da cena atual, para mapearmos ids reais sem adivinhar.
    /// Nada no jogo ou no save é alterado. Saída: BepInEx/config/AutoKeeper/dumps/.
    /// </summary>
    internal static partial class GameApi
    {
        private const int MaxWgosInDump = 1500;

        private static readonly string[] BodyKeywords =
            { "body", "corpse", "grave", "bury", "burial", "autopsy", "embalm", "cremat", "exhum", "coffin", "organ" };

        public static string WriteDiscoveryDump() => Safe(WriteDiscoveryDumpImpl, null, nameof(WriteDiscoveryDump));

        private static string WriteDiscoveryDumpImpl()
        {
            if (!IsInGame)
            {
                ModLog.Warn("Dump: carregue um save primeiro (não funciona no menu).");
                return null;
            }

            PlayerData pd = MainGame.PlayerData;
            string sceneId = pd.currentGameSceneId;
            Vector3 playerPos = pd.position.Value;

            var root = new JObject
            {
                ["modVersion"] = Plugin.Version,
                ["gameVersion"] = GetGameVersion(),
                ["createdAt"] = DateTime.Now.ToString("s"),
                ["player"] = new JObject
                {
                    ["sceneId"] = sceneId,
                    ["position"] = Vec(playerPos),
                    ["energy"] = GetEnergy(),
                    ["energyMax"] = GetEnergyMax(),
                    ["insanity"] = GetPlayerRes("insanity"),
                    ["timeOfDay"] = GetTimeOfDay(),
                    ["day"] = GetDay(),
                    ["blockReason"] = GetBlockReason(),
                    ["overhead"] = ItemsToJson(pd.OverheadItems, 2),
                    ["inventory"] = ItemsToJson(pd.inventory.Data.Inventory, 1),
                    ["toolBelt"] = ItemsToJson(pd.toolBeltInventory.Data.Inventory, 0),
                },
            };

            GameSceneData scene = MainGame.WorldData.GetGameSceneDataById(sceneId);
            var craftsByWgoDef = new JObject();
            var wgos = new JArray();
            int skipped = 0;

            IEnumerable<WgoData> ordered = scene.wgoDataList
                .Where(w => w != null)
                .OrderBy(w => Vector3.Distance(w.Position, playerPos));

            foreach (WgoData w in ordered)
            {
                if (wgos.Count >= MaxWgosInDump)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    wgos.Add(WgoToJson(w, playerPos, craftsByWgoDef));
                }
                catch (Exception e)
                {
                    wgos.Add(new JObject { ["id"] = w.id, ["error"] = e.GetType().Name + ": " + e.Message });
                }
            }

            var drops = new JArray();
            foreach (DropData d in scene.droppedItems.Where(d => d != null).OrderBy(d => Vector3.Distance(d.Position, playerPos)))
            {
                var j = ItemToJson(d.Item, 2);
                j["position"] = Vec(d.Position);
                j["distance"] = Round(Vector3.Distance(d.Position, playerPos));
                drops.Add(j);
            }

            root["scene"] = new JObject
            {
                ["id"] = sceneId,
                ["wgoTotal"] = scene.wgoDataList.Count,
                ["wgoSkipped"] = skipped,
                ["wgos"] = wgos,
                ["drops"] = drops,
                ["craftsByWgoDef"] = craftsByWgoDef,
            };
            root["bodyRelatedCraftDefs"] = BodyRelatedCraftDefs();
            root["bodyItemDefs"] = BodyItemDefs();
            root["defsOfInterest"] = DefsOfInterest();

            string dir = Path.Combine(Paths.ConfigPath, "AutoKeeper", "dumps");
            Directory.CreateDirectory(dir);
            string safeScene = string.Concat((sceneId ?? "scene").Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(dir, $"dump-{safeScene}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, root.ToString(Formatting.Indented));
            return path;
        }

        private static JObject WgoToJson(WgoData w, Vector3 playerPos, JObject craftsByWgoDef)
        {
            WGODef def = w.Definition;
            var j = new JObject
            {
                ["id"] = w.id,
                ["uid"] = w.UniqueId?.ToString(),
                ["interactionType"] = def != null ? def.interactionType.ToString() : "?",
                ["wgoGroup"] = def?.wgoGroup,
                ["customTag"] = w.CustomTag,
                ["position"] = Vec(w.Position),
                ["distance"] = Round(Vector3.Distance(w.Position, playerPos)),
            };

            IWorker worker = w.Worker;
            if (worker != null)
            {
                j["worker"] = worker.GetType().Name;
            }

            CraftComponent cc = w.CraftComponent;
            if (cc != null && (cc.IsStarted || cc.HasCraftsInQueue))
            {
                j["craft"] = new JObject
                {
                    ["isStarted"] = cc.IsStarted,
                    ["status"] = cc.Status.ToString(),
                    ["current"] = cc.CurrentCraftElement?.Def?.id,
                    ["queue"] = new JArray(cc.CraftElementsQueue.Select(e => (object)e?.Def?.id)),
                };
            }

            if (w.Inventory?.Data?.Inventory != null && w.Inventory.Data.Inventory.Count > 0)
            {
                j["inventory"] = ItemsToJson(w.Inventory.Data.Inventory, 2);
            }

            // Receitas: registradas uma vez por tipo de WGO para não repetir.
            if (def != null && craftsByWgoDef[w.id] == null && cc != null && cc.HasCraftsByBalance)
            {
                var crafts = new JArray();
                foreach (CraftDefBase c in cc.CraftsIn)
                {
                    crafts.Add(CraftToJson(c));
                }
                craftsByWgoDef[w.id] = crafts;
            }
            return j;
        }

        private static JObject CraftToJson(CraftDefBase c)
        {
            var j = new JObject
            {
                ["id"] = c.id,
                ["isAuto"] = c.isAuto,
                ["isAutopsyCraft"] = c.isAutopsyCraft,
                ["needItems"] = NeedItemsToJson(c.needItems),
                ["needItemsFromWgo"] = NeedItemsToJson(c.needItemsFromWgo),
            };
            if (c is CraftDef cd)
            {
                j["autopsyTypeCraft"] = cd.autopsyTypeCraft.ToString();
                j["autopsyItemId"] = cd.autopsyItemId;
                j["replaceWgoId"] = cd.replaceWgoId;
                j["isOneTimeCraft"] = cd.isOneTimeCraft;
                j["customItemTypeAction"] = cd.customItemTypeAction.ToString();
                j["craftsIn"] = new JArray(cd.craftsIn);
            }
            return j;
        }

        private static JArray NeedItemsToJson(List<NeedItemData> needs)
        {
            var arr = new JArray();
            if (needs == null)
            {
                return arr;
            }
            foreach (NeedItemData n in needs)
            {
                arr.Add(new JObject
                {
                    ["id"] = n.id,
                    ["count"] = n.count?.ToString(),
                    ["group"] = n.groupType.ToString(),
                });
            }
            return arr;
        }

        private static JArray BodyRelatedCraftDefs()
        {
            var arr = new JArray();
            foreach (CraftDef c in GameBalance.Me.GetDataCollection<CraftDef>())
            {
                if (c == null)
                {
                    continue;
                }
                bool relevant = c.isAutopsyCraft || c.autopsyTypeCraft != AutopsyTypeCraft.None || ContainsKeyword(c.id)
                    || c.needItems.Any(n => ContainsKeyword(n.id));
                if (relevant)
                {
                    arr.Add(CraftToJson(c));
                }
            }
            return arr;
        }

        private static readonly string[] DefKeywords =
            { "grave", "pallet", "cremat", "morgue", "tp_", "embalm", "autopsy", "river_body" };

        /// <summary>Definições de WGO relevantes (mesmo as que não existem na cena), com interações e receitas.</summary>
        private static JArray DefsOfInterest()
        {
            var arr = new JArray();
            foreach (WGODef d in GameBalance.Me.GetDataCollection<WGODef>())
            {
                if (d?.id == null || !DefKeywords.Any(k => d.id.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }
                var crafts = new JArray();
                if (GameBalance.Me.craftsInCache.TryGetValue(d.id, out List<CraftDefBase> list))
                {
                    foreach (CraftDefBase c in list)
                    {
                        crafts.Add(CraftToJson(c));
                    }
                }
                arr.Add(new JObject
                {
                    ["id"] = d.id,
                    ["interactionType"] = d.interactionType.ToString(),
                    ["wgoGroup"] = d.wgoGroup,
                    ["toolAction"] = d.toolAction?.actionableTool.ToString(),
                    ["customInteraction"] = CustomInteractionToJson(d.customInteraction),
                    ["customInteraction2"] = CustomInteractionToJson(d.customInteraction2),
                    ["crafts"] = crafts,
                });
            }
            return arr;
        }

        private static JToken CustomInteractionToJson(CustomInteraction ci)
        {
            if (ci == null)
            {
                return null;
            }
            return new JObject
            {
                ["hint"] = ci.hint,
                ["condition"] = ci.condition?.ToString(),
                ["execution"] = new JArray((ci.execution ?? new List<LazyExpression>()).Select(e => (object)e?.ToString())),
            };
        }

        private static JArray BodyItemDefs()
        {
            var arr = new JArray();
            foreach (ItemDef d in GameBalance.Me.GetDataCollection<ItemDef>())
            {
                if (d?.itemGroupIds != null && (d.itemGroupIds.Contains("body") || ContainsKeyword(d.id)))
                {
                    arr.Add(new JObject
                    {
                        ["id"] = d.id,
                        ["type"] = d.type.ToString(),
                        ["size"] = d.itemSize.ToString(),
                        ["groups"] = new JArray(d.itemGroupIds),
                    });
                }
            }
            return arr;
        }

        private static JArray ItemsToJson(IEnumerable<Item> items, int depth)
        {
            var arr = new JArray();
            if (items == null)
            {
                return arr;
            }
            foreach (Item it in items)
            {
                if (it != null && !it.IsEmpty)
                {
                    arr.Add(ItemToJson(it, depth));
                }
            }
            return arr;
        }

        private static JObject ItemToJson(Item it, int depth)
        {
            ItemDef def = it.Definition;
            var j = new JObject
            {
                ["id"] = it.id,
                ["count"] = it.Count,
            };
            if (def != null)
            {
                j["type"] = def.type.ToString();
                j["size"] = def.itemSize.ToString();
                j["groups"] = new JArray(def.itemGroupIds ?? new List<string>());
            }
            if (depth > 0 && it.Inventory != null && it.Inventory.Count > 0)
            {
                j["children"] = ItemsToJson(it.Inventory, depth - 1);
            }
            return j;
        }

        private static bool ContainsKeyword(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            foreach (string k in BodyKeywords)
            {
                if (id.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static JArray Vec(Vector3 v) => new JArray(Round(v.x), Round(v.y), Round(v.z));

        private static double Round(float f) => Math.Round(f, 2);
    }
}

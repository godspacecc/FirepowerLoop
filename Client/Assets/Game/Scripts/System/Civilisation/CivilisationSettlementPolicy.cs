using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>只转换文明远征的候选存档；不落盘、不切图，远征 ID 去重由 SaveSystem 负责。</summary>
    public static class CivilisationSettlementPolicy
    {
        /// <summary>完整验证与计算后一次替换相关分区；任何错误都保留传入基地原状。</summary>
        public static bool TryApply(BaseSaveData basis, ExpeditionSaveData journey, bool defeated, IRuntimeConfigProvider config, out string error)
        {
            error = null;
            if (basis == null || journey == null || config == null || !config.TryGetCivilisationCatalog(out var catalog, out error)) return false;
            try
            {
                var techModel = new CivilisationTechnologyModel();
                basis.Sections.TryGetValue(CivilisationTechnologyModel.Section, out var oldTech);
                if (!techModel.Validate(oldTech, out error)) return false;
                var technology = oldTech == null ? CivilisationTechnologyModel.Empty() : (JObject)oldTech.DeepClone();
                if (defeated) return true;
                journey.Sections.TryGetValue(CivilisationExpeditionModel.Section, out var rawJourney);
                var journeyModel = new CivilisationExpeditionModel();
                if (!journeyModel.Validate(rawJourney, out error)) return false;
                // 结算仅读取已验证的候选 DTO；不恢复未绑定架构的 Model，也不发送运行时恢复事件。
                var carried = rawJourney ?? CivilisationExpeditionModel.Empty();
                // 日志与科技共用成功返程事务；未知正文拒绝整批提交，重复发现只归档一次。
                var records = new HashSet<string>((technology["records"] as JArray ?? new JArray()).Values<string>(), StringComparer.Ordinal);
                foreach (var key in (carried["records"] as JArray ?? new JArray()).Values<string>())
                {
                    var known = journey.MapId == MapIds.CivilisationValley &&
                        (key == "civilisation.clue.upstream.record" || key == "civilisation.clue.camp.record" ||
                         catalog.Points.Any(p => p.MapId == journey.MapId && (!string.IsNullOrEmpty(p.TechnologyId) || !string.IsNullOrEmpty(p.ChapterId)) && p.NameKey + ".record" == key));
                    if (!known) { error = "未知探索记录：" + key; return false; }
                    records.Add(key);
                }
                var completedChapters = new HashSet<string>((technology["completedChapters"] as JArray ?? new JArray()).Values<string>(), StringComparer.Ordinal);
                var newChapters = new JArray();
                foreach (var id in (carried["chapters"] as JArray ?? new JArray()).Values<string>())
                {
                    if (!catalog.Points.Any(p => p.MapId == journey.MapId && p.ChapterId == id))
                    { error = "未知章节凭证：" + id; return false; }
                    if (completedChapters.Add(id)) newChapters.Add(id);
                }
                var unlocked = new HashSet<string>(technology["unlocked"].Values<string>(), StringComparer.Ordinal);
                var newTech = new JArray();
                foreach (var id in carried["technologies"].Values<string>())
                {
                    if (!catalog.Technologies.Any(t => t.Id == id)) { error = "未知文明科技：" + id; return false; }
                    if (unlocked.Add(id)) newTech.Add(id);
                }
                if (!journey.Sections.TryGetValue("another-day.player-bag", out var rawBag) || !(rawBag is JObject bag) ||
                    bag["capacity"]?.Type != JTokenType.Integer ||
                    !InventorySaveContracts.ValidateSlots(bag["slots"], bag["capacity"].Value<int>(), config, out var slots, out error)) return false;
                // 口袋与背包承担相同远征兑换规则，不能通过移动到口袋绕过结算。
                if (!InventorySaveContracts.ValidateSlots(bag["pocketSlots"] ?? new JArray(), InventoryDefaults.PlayerPocketCapacity,
                    config, out var pocketSlots, out error)) return false;
                slots.AddRange(pocketSlots);
                if (!basis.Sections.TryGetValue(CurrencyModel.SaveSectionId, out var rawGold) || !new CurrencyModel().Validate(rawGold, out error)) return false;
                long total = 0;
                var rows = new JArray();
                var exempt = carried["freeFoodRemaining"].Value<int>();
                foreach (var group in slots.GroupBy(s => s.ItemId))
                {
                    if (!catalog.ReturnValues.TryGetValue(group.Key, out var price)) { error = "没有兑换规则：" + group.Key; return false; }
                    var amount = checked(group.Sum(s => s.Amount));
                    var excluded = group.Key == 29 ? Math.Min(amount, exempt) : 0;
                    var value = checked((long)(amount - excluded) * price);
                    total = checked(total + value);
                    rows.Add(new JObject { ["itemId"] = group.Key, ["amount"] = amount, ["excluded"] = excluded, ["gold"] = value });
                }
                var gold = (JObject)rawGold.DeepClone();
                gold["gold"] = checked(rawGold["gold"].Value<long>() + total);
                // 按现行背包分区生成结算候选，不把旧存档的废弃字段带回基地。
                var emptyBag = new JObject
                {
                    ["capacity"] = bag["capacity"].DeepClone(),
                    ["slots"] = new JArray(),
                    ["pocketSlots"] = new JArray(),
                    ["riskState"] = "BaseSafe"
                };
                technology["unlocked"] = new JArray(unlocked.OrderBy(id => id, StringComparer.Ordinal));
                technology["records"] = new JArray(records.OrderBy(id => id, StringComparer.Ordinal));
                technology["completedChapters"] = new JArray(completedChapters.OrderBy(id => id, StringComparer.Ordinal));
                technology["receipt"] = new JObject
                {
                    ["expeditionId"] = journey.ExpeditionId, ["gold"] = total, ["rows"] = rows,
                    ["newTechnologies"] = newTech, ["newChapters"] = newChapters, ["visited"] = carried["visited"].DeepClone(), ["acknowledged"] = false
                };
                // 此处之前只有候选对象发生变化；提交后清空武器身份，避免引用已兑换的装备。
                basis.Sections[CurrencyModel.SaveSectionId] = gold;
                basis.Sections["another-day.player-bag"] = emptyBag;
                basis.Sections[WeaponEquipmentModel.Section] = new WeaponEquipmentModel().Capture();
                basis.Sections[CivilisationTechnologyModel.Section] = technology;
                return true;
            }
            catch (Exception e) { error = "文明远征结算失败：" + e.Message; return false; }
        }
    }
}

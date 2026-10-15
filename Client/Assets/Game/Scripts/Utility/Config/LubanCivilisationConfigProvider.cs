using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game
{
    public sealed partial class LubanRuntimeConfigProvider
    {
        /// <summary>当前 Provider 所属表集的已校验只读快照；不随逐点查询重新分配，失败不缓存。</summary>
        private CivilisationCatalog civilisationCatalog;
        /// <summary>验证文明表的全部引用后提供只读快照；不以硬编码数值补救错误配置。</summary>
        public bool TryGetCivilisationCatalog(out CivilisationCatalog value, out string error)
        {
            value = null;
            if (!EnsureValid(out error)) return false;
            if (civilisationCatalog != null) { value = civilisationCatalog; error = null; return true; }
            try
            {
                var tech = Tables.TbCivilisationTechnology.DataList.Select(t => new CivilisationTechnologyDefinition(t.Id, t.NameKey, t.UnlockKey)).ToArray();
                var production = Tables.TbCivilisationProduction.DataList.Select(t => new CivilisationProductionDefinition(t.Id, t.TechnologyId, t.OutputItemId, t.CostGold)).ToArray();
                var prices = Tables.TbCivilisationReturnValue.DataList.ToDictionary(t => t.ItemId, t => t.ValueGold);
                var points = Tables.TbCivilisationSearchPoint.DataList.Select(t => new CivilisationSearchPointDefinition(t.Id, t.MapId, t.TechnologyId, t.ItemId, t.Amount, t.SearchSeconds, t.PositionX, t.PositionZ, t.NameKey, t.RequiredWeaponItemId, t.FellTree, t.Repeatable, t.ChapterId)).ToArray();
                if (tech.Any(t => string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.NameKey) || string.IsNullOrWhiteSpace(t.UnlockKey)) ||
                    prices.Any(p => p.Value < 0 || Tables.TbItem.GetOrDefault(p.Key) == null) ||
                    production.Any(p => !tech.Any(t => t.Id == p.TechnologyId) || Tables.TbItem.GetOrDefault(p.OutputItemId) == null || p.CostGold <= 0 || !prices.ContainsKey(p.OutputItemId) || prices[p.OutputItemId] > p.CostGold) ||
                    points.Any(p => Tables.TbMap.GetOrDefault(p.MapId) == null || p.Seconds <= 0 || float.IsNaN(p.Seconds) || float.IsInfinity(p.Seconds) ||
                        (!string.IsNullOrEmpty(p.TechnologyId) && !tech.Any(t => t.Id == p.TechnologyId)) ||
                        (p.ItemId != 0 && (p.Amount <= 0 || !prices.ContainsKey(p.ItemId))) || (p.ItemId == 0 && string.IsNullOrEmpty(p.TechnologyId) && string.IsNullOrEmpty(p.ChapterId)) ||
                        (!string.IsNullOrEmpty(p.ChapterId) && (string.IsNullOrWhiteSpace(p.ChapterId) || p.Repeatable || p.FellTree)) ||
                        p.RequiredWeaponItemId < 0 || (p.RequiredWeaponItemId != 0 && Tables.TbWeapon.GetOrDefault(p.RequiredWeaponItemId)?.WeaponKind != global::WeaponKind.Melee) ||
                        (p.FellTree && (p.RequiredWeaponItemId == 0 || p.ItemId == 0)) ||
                        (p.Repeatable && (p.FellTree || p.ItemId == 0 || !string.IsNullOrEmpty(p.TechnologyId))) ||
                        float.IsNaN(p.Position.x) || float.IsInfinity(p.Position.x) || float.IsNaN(p.Position.z) || float.IsInfinity(p.Position.z)))
                { error = "文明探索配置引用、成本或搜索点无效"; return false; }
                value = new CivilisationCatalog(Array.AsReadOnly(tech), Array.AsReadOnly(production), new ReadOnlyDictionary<int, int>(prices), Array.AsReadOnly(points));
                civilisationCatalog = value;
                error = null;
                return true;
            }
            catch (Exception e) { error = "文明探索配置无效：" + e.Message; return false; }
        }
    }
}

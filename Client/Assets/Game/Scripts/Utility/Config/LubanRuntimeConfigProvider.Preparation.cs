using System;
using System.Linq;

namespace Game
{
    public sealed partial class LubanRuntimeConfigProvider
    {
        private BasePreparationRule preparationRuleCache;
        /// <summary>读取客户端准备规则并验证全部覆盖；缺行不能以零重量或无限制放行。</summary>
        public bool TryGetPreparationRule(out BasePreparationRule rule,out string error)
        {
            rule=preparationRuleCache;
            if(rule!=null){error=null;return true;}
            if(!EnsureValid(out error))return false;
            var row=Tables.TbBasePreparationRule.GetOrDefault("base");
            if(row==null||Tables.TbBasePreparationRule.DataList.Count!=1){error="TbBasePreparationRule 必须有唯一base规则";return false;}
            rule=new BasePreparationRule(row.GameMinutesPerRealSecond,row.StartMinuteOfDay,row.DailyGold,row.RestMinuteOfDay,row.DepartureWeightGrams,row.NormalWeightGrams,row.SpeedPenaltyPerKg,row.SprintCostIncreasePerKg,row.MinimumSpeedMultiplier,row.MaximumSprintCostMultiplier);
            if(!Finite(rule.MinutesPerSecond)||rule.MinutesPerSecond<=0||rule.StartMinute<0||rule.StartMinute>=1440||rule.RestMinute<0||rule.RestMinute>=1440||rule.DailyGold<=0||rule.DepartureGrams<=0||rule.NormalGrams<=0||!Finite(rule.SpeedPenalty)||rule.SpeedPenalty<0||!Finite(rule.SprintIncrease)||rule.SprintIncrease<0||!Finite(rule.MinimumSpeed)||rule.MinimumSpeed<=0||rule.MinimumSpeed>1||!Finite(rule.MaximumSprint)||rule.MaximumSprint<1)
            {error="TbBasePreparationRule[base] 数值无效";rule=null;return false;}
            foreach(var item in Tables.TbItem.DataList)
                if(!TryGetItemPreparation(item.Id,out _,out error)){rule=null;return false;}
            foreach(var map in Tables.TbMap.DataList)
                if(!TryGetMapPreparation(map.Id,out _,out error)){rule=null;return false;}
            preparationRuleCache=rule;error=null;return true;
        }
        public bool TryGetItemPreparation(int id,out ItemPreparationDefinition item,out string error)
        {
            item=default;if(!EnsureValid(out error))return false;
            var row=Tables.TbItemPreparation.GetOrDefault(id);
            if(row==null||row.WeightGrams<=0||Tables.TbItem.GetOrDefault(id)==null||!new[]{"Traditional","Modern","Future","MapData"}.Contains(row.TechnologyCategory))
            {error=$"TbItemPreparation[{id}] 重量或技术分类无效";return false;}
            item=new ItemPreparationDefinition(id,row.WeightGrams,row.TechnologyCategory);error=null;return true;
        }
        public bool TryGetMapPreparation(string id,out MapPreparationRule rule,out string error)
        {
            rule=null;if(!EnsureValid(out error))return false;
            var row=Tables.TbMapPreparationRule.GetOrDefault(id);
            if(row==null||Tables.TbMap.GetOrDefault(id)==null){error=$"TbMapPreparationRule[{id}] 不存在";return false;}
            var allowed=(row.AllowedCategories??"").Split(new[]{'|'},StringSplitOptions.RemoveEmptyEntries);
            if(row.RestrictTechnology&&(allowed.Length==0||allowed.Any(x=>!new[]{"Traditional","Modern","Future","MapData"}.Contains(x))))
            {error=$"TbMapPreparationRule[{id}] 准入分类无效";return false;}
            rule=new MapPreparationRule(row.RestrictTechnology,Array.AsReadOnly(allowed));error=null;return true;
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
    }
}

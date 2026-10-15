using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    public readonly struct CarryLoadSnapshot
    {
        public CarryLoadSnapshot(long grams,long limit,double speed,double sprint,IReadOnlyList<string> forbidden,string error){Grams=grams;Limit=limit;Speed=speed;Sprint=sprint;Forbidden=forbidden;Error=error;}
        public long Grams {get;}
        public long Limit {get;}
        public double Speed {get;}
        public double Sprint {get;}
        public IReadOnlyList<string> Forbidden {get;}
        public string Error {get;}
        public bool CanDepart=>Error==null&&Grams<=Limit&&Forbidden.Count==0;
    }
    /// <summary>统一计量背包、口袋、穿戴和装填弹药；手持不另计，仓库与拾取容量不属于本系统。</summary>
    public sealed class CarryLoadSystem : AbstractSystem
    {
        protected override void OnInit(){}
        public CarryLoadSnapshot Query(string mapId)
        {
            var config=this.GetUtility<IRuntimeConfigProvider>();
            var forbidden=new List<string>();long weight=0;
            if(!config.TryGetPreparationRule(out var rule,out var error)||!config.TryGetMapPreparation(mapId,out var map,out error))return new CarryLoadSnapshot(0,0,1,1,forbidden,error);
            try
            {
                var bags=this.GetModel<PlayerBagModel>();
                foreach(var container in new[]{bags.Container,bags.Pocket,this.GetModel<ClothingEquipmentModel>().Container})
                    foreach(var stack in container.Slots.Values)
                    {
                        Add(stack.ItemId,stack.Amount);
                        if(stack.LoadedAmmo>0)
                        {if(!config.TryGetWeapon(stack.ItemId,out var weapon,out error))throw new InvalidOperationException(error);Add(weapon.AmmoItemId,stack.LoadedAmmo);}
                    }
                void Add(int id,int amount)
                {
                    if(!config.TryGetItemPreparation(id,out var info,out error)||!config.TryGetItem(id,out var item,out error))throw new InvalidOperationException(error);
                    weight=checked(weight+(long)info.Grams*amount);
                    if(map.Restricted&&!map.Allowed.Contains(info.Category))forbidden.Add(Loc.T("ui.base_prep.forbidden_item",Loc.T(item.NameKey),amount));
                }
                return new CarryLoadSnapshot(weight,rule.DepartureGrams,PreparationMath.MoveMultiplier(weight,rule.NormalGrams,rule.SpeedPenalty,rule.MinimumSpeed),PreparationMath.SprintMultiplier(weight,rule.NormalGrams,rule.SprintIncrease,rule.MaximumSprint),forbidden.AsReadOnly(),null);
            }
            catch(Exception e){return new CarryLoadSnapshot(0,rule.DepartureGrams,1,1,forbidden,e.Message);}
        }
        public bool TryCheckDeparture(string mapId,out string error)
        {
            var result=Query(mapId);error=result.Error;
            if(error!=null)return false;
            var failures=new List<string>();
            if(result.Grams>result.Limit)failures.Add(Loc.T("ui.base_prep.overweight",result.Grams/1000.0,result.Limit/1000.0));
            if(result.Forbidden.Count>0)failures.Add(Loc.T("ui.base_prep.forbidden",string.Join("、",result.Forbidden)));
            error=string.Join("\n",failures);return failures.Count==0;
        }
        public CarryLoadSnapshot Current=>Query(this.GetModel<MapSceneModel>().CurrentMapId);
        public bool AppliesInCurrentMap=>this.GetModel<SaveModel>().CurrentDomain==OfflineSaveDomain.Expedition&&this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OfflineFull;
    }
}

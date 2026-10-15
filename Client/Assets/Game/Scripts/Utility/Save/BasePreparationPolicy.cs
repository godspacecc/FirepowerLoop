using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>准备分区初始化与返程合并的纯候选转换；调用者负责保存、回滚及事件。</summary>
    public static class BasePreparationPolicy
    {
        public static bool Initialize(BaseSaveData data,IRuntimeConfigProvider config,bool fresh,out string error)
        {
            error=null;
            if(data?.Sections==null){error="缺少基地分区";return false;}
            if(!data.Sections.TryGetValue(CurrencyModel.SaveSectionId,out var currency)||!new CurrencyModel().Validate(currency,out error))return false;
            if(!fresh&&data.Sections.TryGetValue(BasePreparationModel.Section,out var existing)&&existing!=null)
            {
                if(!new BasePreparationModel().Validate(existing,out error))return false;
                if(!data.Sections.TryGetValue(ShopUnlockModel.Section,out var open)||open==null){error="缺少永久商品开放分区";return false;}
                return new ShopUnlockModel().Validate(open,out error);
            }
            if(!fresh&&data.Version!=4){error="新版本存档缺少基地准备分区";return false;}
            if(!config.TryGetPreparationRule(out var rule,out error)||!config.TryGetShopItems("shop.base.supplies",out var goods,out error))return false;
            try
            {
                var unlocked=new HashSet<int>();
                if(!fresh)
                    foreach(var key in new[]{"another-day.player-bag","another-day.clothing-equipment","another-day.warehouse"})
                        if(data.Sections.TryGetValue(key,out var section)&&section!=null)
                            foreach(var slots in new[]{section["slots"],section["pocketSlots"]})
                                foreach(var slot in slots as JArray??new JArray())
                                    if(goods.Any(g=>g.ItemId==slot["itemId"].Value<int>()&&g.BuyPrice>0))unlocked.Add(slot["itemId"].Value<int>());
                if(!data.Sections.TryGetValue(CurrencyModel.SaveSectionId,out var raw)||raw is not JObject gold)throw new FormatException("缺少基地金币");
                var time=rule.StartMinute*60000L;
                var nextGold=(JObject)gold.DeepClone();nextGold["gold"]=fresh?rule.DailyGold:checked(gold["gold"].Value<long>()+rule.DailyGold);
                data.Sections[BasePreparationModel.Section]=new JObject{["schemaVersion"]=1,["totalMilliseconds"]=time,["lastGrantedDay"]=1};
                data.Sections[ShopUnlockModel.Section]=new JObject{["schemaVersion"]=1,["items"]=new JArray(unlocked.OrderBy(x=>x))};
                data.Sections[CurrencyModel.SaveSectionId]=nextGold;data.Version=5;return true;
            }
            catch(Exception e){error="基地准备初始化失败："+e.Message;return false;}
        }
        /// <summary>只合并尚未结算的远征；成功时在兑换清空前识别仍携带的当地来源。</summary>
        public static bool Settle(BaseSaveData basis,ExpeditionSaveData journey,bool defeated,IRuntimeConfigProvider config,out string error)
        {
            var candidate=basis.Clone();
            if(!SettleCandidate(candidate,journey,defeated,config,out error))return false;
            basis.Sections=candidate.Sections;basis.Version=candidate.Version;return true;
        }
        private static bool SettleCandidate(BaseSaveData basis,ExpeditionSaveData journey,bool defeated,IRuntimeConfigProvider config,out string error)
        {
            if(!Initialize(basis,config,false,out error)||!config.TryGetPreparationRule(out var rule,out error))return false;
            try
            {
                foreach(var key in new[]{"another-day.player-bag",ClothingEquipmentModel.Section})
                    if(journey.Sections.TryGetValue(key,out var carried)&&carried!=null)
                        foreach(var rawSlots in new[]{carried["slots"],carried["pocketSlots"]})
                        {
                            if(rawSlots==null)continue;
                            int capacity=key==ClothingEquipmentModel.Section?ClothingEquipmentModel.SlotCount:ReferenceEquals(rawSlots,carried["pocketSlots"])?InventoryDefaults.PlayerPocketCapacity:carried["capacity"].Value<int>();
                            if(!InventorySaveContracts.ValidateSlots(rawSlots,capacity,config,out var slots,out error))return false;
                            if(slots.Any(s=>(s.AcquiredAmount+s.LoadedAcquiredAmount)>0&&s.OriginExpeditionId!=journey.ExpeditionId))
                            {error="库存来源不属于当前远征";return false;}
                        }
                var clock=(JObject)basis.Sections[BasePreparationModel.Section].DeepClone();
                if(journey.Sections.TryGetValue(ExpeditionPreparationModel.Section,out var raw)&&raw!=null)
                {
                    var ledger=new ExpeditionPreparationModel();ledger.Restore(raw);
                    if(ledger.ExpeditionId!=journey.ExpeditionId||journey.ElapsedGameMinutes<ledger.ElapsedAnchor)throw new FormatException("远征锚点身份或耗时不匹配");
                    var time=ledger.Project(journey.ElapsedGameMinutes);
                    if(time<clock["totalMilliseconds"].Value<long>())throw new FormatException("基地时间不可倒退");
                    var gold=(JObject)basis.Sections[CurrencyModel.SaveSectionId].DeepClone();
                    gold["gold"]=checked(gold["gold"].Value<long>()+PreparationMath.DailyCredit(time,clock["lastGrantedDay"].Value<long>(),rule.DailyGold));
                    clock["totalMilliseconds"]=time;clock["lastGrantedDay"]=PreparationMath.Day(time);basis.Sections[CurrencyModel.SaveSectionId]=gold;
                }
                else if(journey.Version>=4)throw new FormatException("缺少远征准备账本");
                var opens=(JObject)basis.Sections[ShopUnlockModel.Section].DeepClone();
                var ids=new HashSet<int>(((JArray)opens["items"]).Values<int>());
                if(!defeated&&journey.Status!=ExpeditionStatus.Defeated)
                {
                    if(!config.TryGetShopItems("shop.base.supplies",out var goods,out error))return false;
                    foreach(var key in new[]{"another-day.player-bag",ClothingEquipmentModel.Section})
                        if(journey.Sections.TryGetValue(key,out var section)&&section!=null)
                            foreach(var slots in new[]{section["slots"],section["pocketSlots"]})
                                foreach(var slot in slots as JArray??new JArray())
                                {
                                    if(slot["originExpeditionId"]?.Value<string>()!=journey.ExpeditionId)continue;
                                    var id=slot["itemId"].Value<int>();
                                    if((slot["acquiredAmount"]?.Value<int>()??0)>0&&goods.Any(g=>g.ItemId==id&&g.BuyPrice>0))ids.Add(id);
                                    if((slot["loadedAcquiredAmount"]?.Value<int>()??0)>0&&config.TryGetWeapon(id,out var weapon,out _)&&goods.Any(g=>g.ItemId==weapon.AmmoItemId&&g.BuyPrice>0))ids.Add(weapon.AmmoItemId);
                                }
                }
                opens["items"]=new JArray(ids.OrderBy(x=>x));basis.Sections[BasePreparationModel.Section]=clock;basis.Sections[ShopUnlockModel.Section]=opens;return true;
            }
            catch(Exception e){error="基地准备结算失败："+e.Message;return false;}
        }
    }
}

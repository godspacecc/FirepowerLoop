using System;
using System.Collections.Generic;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>多实例支援候选与活动几何；不创建Unity对象，不决定目标HP。</summary>
    public static class ArcadeSlayerMartSupportRules
    {
        /// <summary>在克隆候选中一次交付全部支援，失败不发布半份套餐。</summary>
        public static bool TryApplyGrants(ArcadeSlayerMartState state,ArcadeSlayerMartConfiguration config,
            IReadOnlyDictionary<long,int> ordinaryChickens,out ArcadeSlayerMartState candidate,out string error)
        {
            candidate=null;error="invalid_support_grant";
            if(config==null)return false;
            var catalog=config.Supports;Func<string,float> parameter=config.Parameter;
            if(state?.Players==null||state.PendingGrants==null||catalog==null||parameter==null||
                state.Players.Any(p=>p==null||p.Supports==null||p.Queues==null||p.SupportHits==null||p.AppliedSupportGrants==null)||
                state.PendingGrants.Any(g=>g==null)||state.PendingGrants.Select(g=>g.EventId).Distinct(StringComparer.Ordinal).Count()!=state.PendingGrants.Count)return false;
            if(state.Players.Any(p=>!IsValid(p,config)))return false;
            var next=state.Clone();
            // 先在候选中完成上一帧耗尽的交接，避免新套餐另起一条并行爆桶队列。
            foreach(var player in next.Players)Advance(player,0,true,catalog,parameter);
            foreach(var grant in next.PendingGrants)
            {
                if(!IsSupport(grant.Effect))continue;
                var player=next.Players.FirstOrDefault(p=>p.PlayerId==grant.PlayerId);
                if(player==null||string.IsNullOrWhiteSpace(grant.EventId))return false;
                if(player.AppliedSupportGrants.Contains(grant.EventId))continue;
                if(grant.Amount<=0||!Finite(grant.Duration)||grant.Duration<0)return false;
                var product=config.Catalog.Items.Single(p=>p.Effect==grant.Effect);
                if(grant.Amount!=product.Amount||grant.Duration!=product.Duration)return false;
                if(grant.Effect==SlayerMartEffect.Chicken||grant.Effect==SlayerMartEffect.ChickenPack)
                {
                    int ordinary=ordinaryChickens!=null&&ordinaryChickens.TryGetValue(player.PlayerId,out int value)?value:0;
                    int maximum=(int)parameter("inventory.maxChickens");
                    if(ordinary<0||grant.Amount!=(grant.Effect==SlayerMartEffect.Chicken?1:3)||
                        player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+(long)ordinary+grant.Amount>maximum)return false;
                    for(int index=0;index<grant.Amount;index++)
                    {
                        var item=Create(player,grant,SlayerMartSupportKind.Chicken,index,catalog,parameter);
                        for(int slot=0;slot<maximum;slot++)
                        {double phase=slot*Math.PI*2/maximum;if(player.Supports.Any(p=>p.Kind==SlayerMartSupportKind.Chicken&&Math.Abs(p.Phase-phase)<.001))continue;item.Phase=phase;break;}
                        player.Supports.Add(item);
                    }
                }
                else if(grant.Effect==SlayerMartEffect.TeslaPack)
                {
                    if(grant.Amount!=3||player.Supports.Any(p=>p.Kind==SlayerMartSupportKind.Orb&&p.RemainingSeconds>0))return false;
                    for(int index=0;index<3;index++){var item=Create(player,grant,SlayerMartSupportKind.Orb,index,catalog,parameter);item.Phase=index*Math.PI*2/3;player.Supports.Add(item);}
                }
                else if(grant.Effect==SlayerMartEffect.BarrelRelay)
                {
                    if(grant.Amount!=3)return false;
                    var active=player.Supports.FirstOrDefault(p=>p.Kind==SlayerMartSupportKind.Barrel&&p.RemainingSeconds>0&&p.Charges>0);
                    var queue=active==null?null:player.Queues.FirstOrDefault(p=>p.QueueId==active.QueueId);
                    if(queue==null){queue=new ArcadeSlayerMartEffectQueue{QueueId=grant.EventId+":queue"};player.Queues.Add(queue);if(active!=null)active.QueueId=queue.QueueId;}
                    for(int index=0;index<3;index++)
                    {var item=Create(player,grant,SlayerMartSupportKind.Barrel,index,catalog,parameter);item.QueueId=queue.QueueId;
                        if(active==null){active=item;player.Supports.Add(item);}else queue.Waiting.Add(item);}
                }
                else
                {
                    if(grant.Amount!=1)return false;
                    Merge(player,Create(player,grant,SlayerMartSupportKind.Saw,0,catalog,parameter));
                    if(grant.Effect==SlayerMartEffect.SawBarrel)Merge(player,Create(player,grant,SlayerMartSupportKind.Barrel,0,catalog,parameter));
                }
                player.AppliedSupportGrants.Add(grant.EventId);
            }
            foreach(var player in next.Players)
            {
                int ordinary=ordinaryChickens!=null&&ordinaryChickens.TryGetValue(player.PlayerId,out int count)?count:0;
                player.Chickens=player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+ordinary;
            }
            if(next.Players.Any(p=>!IsValid(p,config)))return false;
            candidate=next;error=null;return true;
        }
        /// <summary>只推进当前份，长帧也不扣后续份时间；零步长可完成一次帧末交接。</summary>
        public static void Advance(ArcadeSlayerMartPlayer player,double seconds,bool active,ArcadeSlayerMartSupportCatalog catalog,Func<string,float> parameter)
        {
            if(!active||!Finite(seconds)||seconds<0)return;
            int previousChickenCount=player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0);
            foreach(var item in player.Supports)
            {
                item.RemainingSeconds=Math.Max(0,item.RemainingSeconds-seconds);item.CooldownSeconds=Math.Max(0,item.CooldownSeconds-seconds);
                if(item.Kind==SlayerMartSupportKind.Chicken)continue;
                double speed=parameter("support.orbitSpeed");
                if(item.Kind==SlayerMartSupportKind.Barrel)speed=ArcadeSlayerMartAbilityRules.MasteryOrbitSpeed((float)speed,player,parameter);
                item.Phase=(item.Phase+speed*seconds)%(Math.PI*2);
            }
            foreach(var hit in player.SupportHits.Where(p=>!p.GroupId.StartsWith("ordinary:",StringComparison.Ordinal)))hit.RemainingSeconds=Math.Max(0,hit.RemainingSeconds-seconds);
            player.SupportHits.RemoveAll(p=>p.RemainingSeconds<=0);
            player.Supports.RemoveAll(p=>p.RemainingSeconds<=0||p.Kind==SlayerMartSupportKind.Barrel&&p.Charges<=0);
            player.Chickens=Math.Max(0,player.Chickens-previousChickenCount)+player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0);
            foreach(var queue in player.Queues)
            {
                if(player.Supports.Any(p=>p.QueueId==queue.QueueId)||queue.Waiting.Count==0)continue;
                var item=queue.Waiting[0];queue.Waiting.RemoveAt(0);item.CooldownSeconds=catalog.Get(item.Kind).Interval;player.Supports.Add(item);
            }
            player.Queues.RemoveAll(p=>p.Waiting.Count==0&&!player.Supports.Any(i=>i.QueueId==p.QueueId));
        }
        /// <summary>宿主已确认目标和弹丸预算后，才消费该鸡自己的射击周期。</summary>
        public static bool TryShoot(ArcadeSlayerMartEffectInstance instance,ArcadeSlayerMartSupportCatalog catalog)
        {
            if(instance.Kind!=SlayerMartSupportKind.Chicken||instance.RemainingSeconds<=0||instance.CooldownSeconds>0)return false;
            instance.CooldownSeconds=catalog.Get(instance.Kind).Interval;return true;
        }
        /// <summary>实际接触才消耗一次爆炸，耗尽即结束，不由剩余秒数补回次数。</summary>
        public static bool TryTriggerBarrel(ArcadeSlayerMartEffectInstance instance,bool touching,ArcadeSlayerMartSupportCatalog catalog)
        {
            if(!touching||instance.Kind!=SlayerMartSupportKind.Barrel||instance.RemainingSeconds<=0||instance.Charges<=0||instance.CooldownSeconds>0)return false;
            instance.Charges--;instance.CooldownSeconds=catalog.Get(instance.Kind).Interval;
            if(instance.Charges==0)instance.RemainingSeconds=0;return true;
        }
        /// <summary>领域与表现共用的轨道中心，双锯使用同一身份下两个相反接触体。</summary>
        public static ArcadeSlayerMartPoint[] Bodies(ArcadeSlayerMartPlayer player,ArcadeSlayerMartEffectInstance instance,float ownerX,float ownerY,ArcadeSlayerMartSupportCatalog catalog,Func<string,float> parameter)
        {
            bool doubleSaw=instance.Kind==SlayerMartSupportKind.Saw&&player.Owned.Contains("slayermart.item.11");
            float radius=instance.Kind==SlayerMartSupportKind.Saw?
                ArcadeSlayerMartAbilityRules.MasteryRadius(catalog.Get(instance.Kind).Radius,SlayerMartEffect.SawMastery,player,parameter):parameter("support.orbitRadius");
            var result=new ArcadeSlayerMartPoint[doubleSaw?2:1];
            for(int index=0;index<result.Length;index++)
            {double angle=instance.Phase+index*Math.PI;result[index]=new ArcadeSlayerMartPoint(ownerX+(float)Math.Cos(angle)*radius,ownerY+(float)Math.Sin(angle)*radius);}
            return result;
        }
        /// <summary>只检查真实接触体，同组合对同一地图目标共享命中周期。</summary>
        public static bool TryContact(ArcadeSlayerMartPlayer player,ArcadeSlayerMartEffectInstance instance,string targetKey,float targetX,float targetY,float enemyRadius,
            float ownerX,float ownerY,ArcadeSlayerMartSupportCatalog catalog,Func<string,float> parameter)
        {
            if(instance.RemainingSeconds<=0||string.IsNullOrWhiteSpace(targetKey)||(instance.Kind!=SlayerMartSupportKind.Orb&&instance.Kind!=SlayerMartSupportKind.Saw)||
                player.SupportHits.Any(p=>p.GroupId==instance.GroupId&&p.TargetKey==targetKey&&p.RemainingSeconds>0))return false;
            double radius=parameter("support.contactRadius")+Math.Max(0,enemyRadius);
            bool touches=Bodies(player,instance,ownerX,ownerY,catalog,parameter).Any(p=>(double)(p.X-targetX)*(p.X-targetX)+(double)(p.Y-targetY)*(p.Y-targetY)<=radius*radius);
            if(!touches)return false;
            player.SupportHits.Add(new ArcadeSlayerMartSupportHit{GroupId=instance.GroupId,TargetKey=targetKey,RemainingSeconds=catalog.Get(instance.Kind).Interval});return true;
        }
        /// <summary>只交付支援事件，宝石和下轮加命由各自正式事务消费。</summary>
        private static bool IsSupport(SlayerMartEffect effect)=>effect==SlayerMartEffect.Chicken||effect==SlayerMartEffect.ChickenPack||effect==SlayerMartEffect.Saw||
            effect==SlayerMartEffect.SawBarrel||effect==SlayerMartEffect.TeslaPack||effect==SlayerMartEffect.BarrelRelay;
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        /// <summary>普通槽沿用宿主既有活动时钟，不能因队友复起而只扣寿命不转动。</summary>
        public static void AdvanceOrdinaryOrbits(ArcadeSlayerMartPlayer player,double seconds,Func<string,float> parameter)
        {
            if(player==null||!Finite(seconds)||seconds<0)return;
            player.OrdinarySawPhase=(player.OrdinarySawPhase+parameter("support.orbitSpeed")*seconds)%(Math.PI*2);
            player.OrdinaryBarrelPhase=(player.OrdinaryBarrelPhase+ArcadeSlayerMartAbilityRules.MasteryOrbitSpeed(parameter("support.orbitSpeed"),player,parameter)*seconds)%(Math.PI*2);
            foreach(var hit in player.SupportHits.Where(p=>p.GroupId.StartsWith("ordinary:",StringComparison.Ordinal)))hit.RemainingSeconds=Math.Max(0,hit.RemainingSeconds-seconds);
            player.SupportHits.RemoveAll(p=>p.RemainingSeconds<=0);
        }
        private static bool ValidPhase(double value)=>Finite(value)&&value>=0&&value<Math.PI*2;
        /// <summary>普通槽投影到同一接触几何，不加入购买实例或重赠来源事件。</summary>
        public static ArcadeSlayerMartEffectInstance OrdinaryBody(ArcadeSlayerMartPlayer player,SlayerMartSupportKind kind,double remaining)
            =>new ArcadeSlayerMartEffectInstance{InstanceId="ordinary:"+player.PlayerId+":"+kind,GroupId="ordinary:"+player.PlayerId+":"+kind,
                OwnerId=player.PlayerId,Kind=kind,RemainingSeconds=remaining,Phase=kind==SlayerMartSupportKind.Saw?player.OrdinarySawPhase:player.OrdinaryBarrelPhase};
        /// <summary>按当前正式商品与基础能力配置校验恢复上界，不接受额外时长、次数或冷却。</summary>
        public static bool IsValid(ArcadeSlayerMartPlayer player,ArcadeSlayerMartConfiguration config)
        {
            if(player==null||!ValidPhase(player.OrdinarySawPhase)||!ValidPhase(player.OrdinaryBarrelPhase)||player.Supports==null||player.Queues==null||player.SupportHits==null||player.Owned==null||
                player.AppliedSupportGrants==null||player.Chickens<0||player.Chickens>config.Catalog.MaxChickens||
                player.Supports.Any(p=>p==null)||player.Queues.Any(p=>p==null||p.Waiting==null||p.Waiting.Any(i=>i==null)))return false;
            if(player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken)>config.Catalog.MaxChickens||
                player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Saw)>1||player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Barrel)>1||
                player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Orb) is not (0 or 3)||player.Queues.Count>1||
                player.Queues.Sum(p=>p.Waiting.Count)>config.Catalog.Regions.Count*config.Catalog.Items.Single(p=>p.Effect==SlayerMartEffect.BarrelRelay).Amount)return false;
            var all=player.Supports.Concat(player.Queues.SelectMany(p=>p.Waiting)).ToArray();
            if(all.Select(p=>p.InstanceId).Distinct(StringComparer.Ordinal).Count()!=all.Length)return false;
            foreach(var item in all)
            {
                if(!Enum.IsDefined(typeof(SlayerMartSupportKind),item.Kind)||string.IsNullOrWhiteSpace(item.InstanceId)||item.OwnerId!=player.PlayerId||
                    !Finite(item.RemainingSeconds)||item.RemainingSeconds<0||!Finite(item.Phase)||item.Phase<0||item.Phase>=Math.PI*2||
                    !Finite(item.CooldownSeconds)||item.CooldownSeconds<0||!Finite(item.AnchorX)||!Finite(item.AnchorY)||
                    !Finite(item.ShotDirectionX)||!Finite(item.ShotDirectionY))return false;
                bool sourceMatches=item.Kind==SlayerMartSupportKind.Chicken&&(item.SourceEffect==SlayerMartEffect.Chicken||item.SourceEffect==SlayerMartEffect.ChickenPack)||
                    item.Kind==SlayerMartSupportKind.Orb&&item.SourceEffect==SlayerMartEffect.TeslaPack||
                    item.Kind==SlayerMartSupportKind.Saw&&(item.SourceEffect==SlayerMartEffect.Saw||item.SourceEffect==SlayerMartEffect.SawBarrel)||
                    item.Kind==SlayerMartSupportKind.Barrel&&(item.SourceEffect==SlayerMartEffect.BarrelRelay||item.SourceEffect==SlayerMartEffect.SawBarrel);
                if(!sourceMatches)return false;
                var definition=config.Supports.Get(item.Kind);var product=config.Catalog.Items.Single(p=>p.Effect==item.SourceEffect);
                var mastery=item.Kind==SlayerMartSupportKind.Chicken?SlayerMartEffect.ChickenMastery:item.Kind==SlayerMartSupportKind.Barrel?SlayerMartEffect.BarrelMastery:0;
                float maximum=ArcadeSlayerMartAbilityRules.MasteryDuration(product.Duration>0?product.Duration:definition.Duration,mastery,player,config.Parameter);
                if(item.Kind==SlayerMartSupportKind.Barrel&&item.SourceEffect==SlayerMartEffect.SawBarrel)maximum=product.Duration;
                if(item.ProductId!=definition.ProductId||item.RemainingSeconds>maximum||item.CooldownSeconds>definition.Interval||
                    item.Charges<0||item.Charges>definition.Charges||item.Kind==SlayerMartSupportKind.Barrel&&item.Charges==0&&item.RemainingSeconds>0)return false;
            }
            foreach(var queue in player.Queues)
                if(string.IsNullOrWhiteSpace(queue.QueueId)||queue.Waiting.Any(p=>p.Kind!=SlayerMartSupportKind.Barrel||p.QueueId!=queue.QueueId||p.CooldownSeconds!=0||p.Charges!=config.Supports.Get(p.Kind).Charges))return false;
            if(player.SupportHits.Any(p=>p==null||string.IsNullOrWhiteSpace(p.GroupId)||string.IsNullOrWhiteSpace(p.TargetKey)||!Finite(p.RemainingSeconds)||p.RemainingSeconds<0||
                p.RemainingSeconds>Enum.GetValues(typeof(SlayerMartSupportKind)).Cast<SlayerMartSupportKind>().Max(k=>config.Supports.Get(k).Interval)))return false;
            return true;
        }
        /// <summary>延长同类效果只取更大剩余，不能把已耗次数和冷却补满。</summary>
        private static void Merge(ArcadeSlayerMartPlayer player,ArcadeSlayerMartEffectInstance item)
        {
            var existing=player.Supports.FirstOrDefault(p=>p.Kind==item.Kind&&p.RemainingSeconds>0);
            if(existing==null)player.Supports.Add(item);else existing.RemainingSeconds=Math.Max(existing.RemainingSeconds,item.RemainingSeconds);
        }
        /// <summary>稳定事件与分件序生成身份，创建时才应用时长精通。</summary>
        private static ArcadeSlayerMartEffectInstance Create(ArcadeSlayerMartPlayer player,ArcadeSlayerMartGrant grant,SlayerMartSupportKind kind,int index,ArcadeSlayerMartSupportCatalog catalog,Func<string,float> parameter)
        {
            var definition=catalog.Get(kind);var mastery=kind==SlayerMartSupportKind.Chicken?SlayerMartEffect.ChickenMastery:
                kind==SlayerMartSupportKind.Barrel?SlayerMartEffect.BarrelMastery:0;
            float duration=ArcadeSlayerMartAbilityRules.MasteryDuration(grant.Duration>0?grant.Duration:definition.Duration,mastery,player,parameter);
            // 五分钟组合的爆桶受商品本身的硬时限约束，精通不能把它延长到375秒。
            if(grant.Effect==SlayerMartEffect.SawBarrel&&kind==SlayerMartSupportKind.Barrel)duration=grant.Duration;
            return new ArcadeSlayerMartEffectInstance{InstanceId=grant.EventId+":"+kind+":"+index,GroupId=kind==SlayerMartSupportKind.Barrel?grant.EventId+":"+index:grant.EventId,
                SourceEventId=grant.EventId,SourceEffect=grant.Effect,ProductId=definition.ProductId,OwnerId=player.PlayerId,Kind=kind,RemainingSeconds=duration,Charges=definition.Charges};
        }
    }
}

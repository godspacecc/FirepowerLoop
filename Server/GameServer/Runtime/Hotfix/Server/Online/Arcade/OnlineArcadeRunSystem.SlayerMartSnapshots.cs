using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>公共快照只投影区域和成员状态，不携带现金、券或个人报价。</summary>
        private static void AppendSlayerMartBattleSnapshot(OnlineArcadeBattleRuntime source,OnlineArcadeBattleInfo target)
        {
            target.SlayerMartPrepared=source.SlayerMartRegionPrepared;target.SlayerMartRulesVersion=source.SlayerMartRulesVersion;target.SlayerMartActive=source.SlayerMartReturnWave>0;
            var state=source.SlayerMart;if(state==null)return;
            target.SlayerMartVisit=state.Visit;target.SlayerMartGeneration=state.Generation;target.SlayerMartReady=state.Ready;
            target.SlayerMartClosing=state.Closing;target.SlayerMartSeconds=state.RemainingSeconds;target.SlayerMartRevision=state.Revision;
            if(!target.SlayerMartActive)return;
            foreach(var owner in state.Players)target.SlayerMartMembers.Add(BuildSlayerMartMember(owner));
            var layout=ServerConfig.Instance.SlayerMart.Regions.Single(p=>p.Id==state.Visit);
            target.SlayerMartCashBoxIds.AddRange(Enumerable.Range(1,layout.Boxes.Count).Where(id=>!state.CollectedBoxes.Contains(id)));
        }
        /// <summary>接触体坐标直接来自权威Bodies；购物期间不投影冻结的战斗支援。</summary>
        private static void AppendSlayerMartPlayerSnapshot(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime source,OnlineArcadeBattlePlayerInfo target)
        {
            var state=SlayerMartAbility(battle,source.PlayerId);if(state==null)return;
            target.VestSlots=state.VestSlots;target.Plates=state.Plates;target.HasStorm=state.Owned.Contains("slayermart.item.16");
            target.StormCharge=state.StormCharge;target.StormSeconds=(float)state.StormRemaining;
            if(battle.SlayerMartReturnWave>0||!source.Alive)return;
            foreach(var item in state.Supports.Where(p=>p.RemainingSeconds>0))AppendSlayerMartBodies(state,source,item,target);
            foreach(var kind in new[]{SlayerMartSupportKind.Saw,SlayerMartSupportKind.Barrel})
            {
                var behavior=kind==SlayerMartSupportKind.Saw?ArcadePowerupBehavior.Saw:ArcadePowerupBehavior.Barrel;
                if(state.Supports.Any(p=>p.Kind==kind&&p.RemainingSeconds>0)||!TryGetPowerup(source,behavior,out var powerup)||kind==SlayerMartSupportKind.Barrel&&powerup.Charges<=0)continue;
                var item=ArcadeSlayerMartSupportRules.OrdinaryBody(state,kind,powerup.RemainingSeconds);
                item.ProductId=powerup.ProductId;item.Charges=powerup.Charges;AppendSlayerMartBodies(state,source,item,target);
            }
        }
        private static void AppendSlayerMartBodies(ArcadeSlayerMartPlayer owner,OnlineArcadeBattlePlayerRuntime source,ArcadeSlayerMartEffectInstance effect,OnlineArcadeBattlePlayerInfo target)
        {
            var config=ServerConfig.Instance.SlayerMart;
            var bodies=ArcadeSlayerMartSupportRules.Bodies(owner,effect,source.PositionX,source.PositionZ,config.Supports,config.Parameter);
            for(int index=0;index<bodies.Length;index++)
            {
                var body=OnlineSlayerMartSupportInfo.Create();body.ProductId=effect.ProductId;body.InstanceId=effect.InstanceId;body.Kind=(int)effect.Kind;body.BodyIndex=index;
                body.PositionX=bodies[index].X;body.PositionZ=bodies[index].Y;body.ShotDirectionX=effect.ShotDirectionX;body.ShotDirectionZ=effect.ShotDirectionY;
                body.RemainingSeconds=(float)effect.RemainingSeconds;body.Charges=effect.Charges;target.SlayerMartSupports.Add(body);
            }
        }
        /// <summary>角色快照中所有支援身份和坐标深拷贝，转发链不能共享可变列表。</summary>
        private static void CloneSlayerMartPlayerSnapshot(OnlineArcadeBattlePlayerInfo source,OnlineArcadeBattlePlayerInfo target)
        {
            target.VestSlots=source.VestSlots;target.Plates=source.Plates;target.HasStorm=source.HasStorm;target.StormCharge=source.StormCharge;target.StormSeconds=source.StormSeconds;
            foreach(var item in source.SlayerMartSupports)
            {
                var body=OnlineSlayerMartSupportInfo.Create();body.ProductId=item.ProductId;body.InstanceId=item.InstanceId;body.Kind=item.Kind;body.BodyIndex=item.BodyIndex;
                body.PositionX=item.PositionX;body.PositionZ=item.PositionZ;body.ShotDirectionX=item.ShotDirectionX;body.ShotDirectionZ=item.ShotDirectionZ;
                body.RemainingSeconds=item.RemainingSeconds;body.Charges=item.Charges;target.SlayerMartSupports.Add(body);
            }
        }
        private static OnlineSlayerMartMemberInfo CloneSlayerMartMember(OnlineSlayerMartMemberInfo source)
        {var copy=OnlineSlayerMartMemberInfo.Create();copy.PlayerId=source.PlayerId;copy.Choice=source.Choice;copy.MenuOpen=source.MenuOpen;copy.Finished=source.Finished;copy.Lives=source.Lives;return copy;}
        /// <summary>公共区域元数据深拷贝，不从个人报价反推队友信息。</summary>
        private static void CloneSlayerMartBattleSnapshot(OnlineArcadeBattleInfo source,OnlineArcadeBattleInfo target)
        {
            target.SlayerMartRulesVersion=source.SlayerMartRulesVersion;target.SlayerMartActive=source.SlayerMartActive;target.SlayerMartVisit=source.SlayerMartVisit;
            target.SlayerMartPrepared=source.SlayerMartPrepared;target.SlayerMartGeneration=source.SlayerMartGeneration;target.SlayerMartReady=source.SlayerMartReady;target.SlayerMartClosing=source.SlayerMartClosing;
            target.SlayerMartSeconds=source.SlayerMartSeconds;target.SlayerMartRevision=source.SlayerMartRevision;target.SlayerMartCashBoxIds.AddRange(source.SlayerMartCashBoxIds);
            foreach(var member in source.SlayerMartMembers)target.SlayerMartMembers.Add(CloneSlayerMartMember(member));
        }
        /// <summary>只有定向响应可能携带个人报价，逐项复制后才跨Actor／会话转发。</summary>
        private static OnlineSlayerMartSnapshotInfo ClonePersonalSlayerMart(OnlineSlayerMartSnapshotInfo source)
        {
            if(source==null)return null;var copy=OnlineSlayerMartSnapshotInfo.Create();
            copy.RegionInstanceId=source.RegionInstanceId;copy.Generation=source.Generation;copy.Revision=source.Revision;copy.Visit=source.Visit;copy.PlayerId=source.PlayerId;
            copy.Cash=source.Cash;copy.Tokens=source.Tokens;copy.RemainingSeconds=source.RemainingSeconds;copy.Ready=source.Ready;copy.Closing=source.Closing;
            copy.Bombs=source.Bombs;copy.Dashes=source.Dashes;copy.Lives=source.Lives;copy.VestSlots=source.VestSlots;copy.Plates=source.Plates;copy.Owned.AddRange(source.Owned);copy.Error=source.Error;
            foreach(var item in source.Offers){var offer=OnlineSlayerMartOfferInfo.Create();offer.ItemId=item.ItemId;offer.Error=item.Error;copy.Offers.Add(offer);}
            foreach(var member in source.Members)copy.Members.Add(CloneSlayerMartMember(member));return copy;
        }
    }
}

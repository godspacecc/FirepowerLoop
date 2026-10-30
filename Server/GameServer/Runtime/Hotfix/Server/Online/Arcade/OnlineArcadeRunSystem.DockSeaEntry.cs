using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>接管码头三路已有队列实体；数量、属性与背桶身份仍由原出生事务负责。</summary>
        private static void InitializeDockSeaEntry(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,int passage)
        {
            var layout=battle.RoundLayout;
            if(layout==null||battle.IsChallenge||battle.IsFinalBoss||battle.RoundBossEncounter)return;
            int entry=passage>=0?passage:battle.SpawnedInWave%layout.Entries.Count;
            if(!ArcadeDockSeaEntryRules.TryStart(layout.ThemeKey,entry,battle.SpawnedInWave,out enemy.DockSeaEntry))return;
            var position=ArcadeDockSeaEntryRules.Position(enemy.DockSeaEntry);
            enemy.PositionX=enemy.HazardPreviousX=position.X;
            enemy.PositionZ=enemy.HazardPreviousZ=position.Y;
        }

        /// <summary>只推进活动年龄；控制、加载与非战斗阶段冻结，站稳完成帧仍不能追击或攻击。</summary>
        private static bool TickDockSeaEntry(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,float seconds)
        {
            if(!enemy.DockSeaEntry.Active)return false;
            CancelMeleeWindup(enemy,true);
            bool frozen=!RoundContentActive(battle)||!RoundPlayersReady(battle)||enemy.HerdImpact.Active||
                enemy.ContentStunUntil>battle.ContentClock.Elapsed||SlayerMartStormSpeed(battle,enemy)==0;
            ArcadeDockSeaEntryRules.Advance(ref enemy.DockSeaEntry,frozen?0:seconds);
            var position=ArcadeDockSeaEntryRules.Position(enemy.DockSeaEntry);
            enemy.PositionX=position.X;
            enemy.PositionZ=position.Y;
            return true;
        }
    }
}

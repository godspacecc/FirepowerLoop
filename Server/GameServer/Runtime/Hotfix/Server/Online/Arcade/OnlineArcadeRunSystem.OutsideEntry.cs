using FirepowerLoop.Arcade;
using System.Linq;
namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>服务端在出生事务末端接管普通队列，快照继续广播真实位置。</summary>
        private static void InitializeOutsideEntry(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,int passage)
        {
            var layout=battle.RoundLayout;
            var arena=ServerConfig.Instance.Tables.TbArcadeArena.Get(battle.ArenaConfigNumber);
            if(ArcadeOutsideRouteRules.HasSpecialArrival(enemy.SpecialKind,enemy.StageEnemy.Phase,
                enemy.ContentBossKind>0||!string.IsNullOrEmpty(enemy.BossId),
                enemy.DockSeaEntry.Started,ArcadeOpeningParameters.FromConfig(arena).Enabled&&!battle.IsChallenge&&!battle.IsFinalBoss))return;
            int entry=layout==null?0:passage>=0?passage:battle.SpawnedInWave%layout.Entries.Count;
            if(layout?.ThemeKey=="docks"&&ArcadeDockSeaEntryRules.TryStart(layout.ThemeKey,entry,battle.SpawnedInWave,out enemy.DockSeaEntry))
            {var p=ArcadeDockSeaEntryRules.Position(enemy.DockSeaEntry);enemy.PositionX=enemy.HazardPreviousX=p.X;enemy.PositionZ=enemy.HazardPreviousZ=p.Y;return;}
            bool started=layout!=null&&ArcadeOutsideRouteRules.TryStart(layout,entry,ArenaHalfWidth(battle),ArenaHalfHeight(battle),
                ServerConfig.Instance.ArcadeRule.EnemyContactRadius,out enemy.OutsideEntry);
            if(!started)started=TryOutsideFallback(battle,enemy.PositionX,enemy.PositionZ,out enemy.OutsideEntry);
            if(!started)throw new System.InvalidOperationException("敌人入口没有合法场外通道，请检查地图配置");
            enemy.PositionX=enemy.HazardPreviousX=enemy.OutsideEntry.StartX;
            enemy.PositionZ=enemy.HazardPreviousZ=enemy.OutsideEntry.StartZ;
        }
        /// <summary>无布局模式按实际障碍选择合法通路，绝不套用主线障碍或在场内补生。</summary>
        private static bool TryOutsideFallback(OnlineArcadeBattleRuntime battle,float x,float z,out ArcadeOutsideEntryState state)
        {
            var row=ServerConfig.Instance.Tables.TbArcadeRoundLayout.DataList.FirstOrDefault(p=>p.ArenaConfigNumber==battle.ArenaConfigNumber);
            var tuning=row==null?null:ArcadeRoundLayoutParameters.FromConfig(row);
            var obstacles=EffectiveObstacles(battle).Select(o=>new ArcadeLayoutObstacle(o.CenterX,o.CenterY,o.HalfWidth,o.HalfHeight)).ToArray();
            return ArcadeOutsideRouteRules.TryStartFallback(tuning,x,z,ArenaHalfWidth(battle),ArenaHalfHeight(battle),
                ServerConfig.Instance.ArcadeRule.EnemyContactRadius,obstacles,out state);
        }
        /// <summary>开场预检兼容场次通道，无法进入时明确拒绝开场。</summary>
        private static bool ValidateOutsideFallback(OnlineArcadeBattleRuntime battle)
        {
            var arena=ServerConfig.Instance.Tables.TbArcadeArena.Get(battle.ArenaConfigNumber);
            return battle.RoundLayout!=null||ArcadeOpeningParameters.FromConfig(arena).Enabled&&!battle.IsChallenge||
                TryOutsideFallback(battle,0,ArenaHalfHeight(battle),out _);
        }
        /// <summary>受控和未准备好时保持场外位置，完成帧不再执行场内移动。</summary>
        private static bool TickOutsideEntry(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,float seconds)
        {
            if(!enemy.OutsideEntry.Active)return false;
            CancelMeleeWindup(enemy,true);
            bool frozen=!RoundPlayersReady(battle)||enemy.HerdImpact.Active||enemy.ContentStunUntil>battle.ContentClock.Elapsed||SlayerMartStormSpeed(battle,enemy)==0;
            ArcadeOutsideEntryRules.Advance(ref enemy.OutsideEntry,frozen?0:seconds,enemy.MoveSpeed*EnemySpeedMultiplier(battle),out var position);
            enemy.PositionX=position.X;enemy.PositionZ=position.Y;return true;
        }
    }
}

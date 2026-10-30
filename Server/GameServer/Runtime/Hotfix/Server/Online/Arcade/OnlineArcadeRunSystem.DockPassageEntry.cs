using System;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>仅普通码头左桥和上通道保留出生位置，首次走入主平台前隔离战斗。</summary>
        private static void InitializeDockPassageEntry(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,int passage)
        {
            var layout=battle.RoundLayout;
            if(layout==null||battle.IsChallenge||battle.IsFinalBoss||battle.RoundBossEncounter)return;
            int entry=passage>=0?passage:battle.SpawnedInWave%layout.Entries.Count;
            enemy.DockPassageEntering=ArcadeDockSeaEntryRules.TryGetDeckEntryTarget(layout.ThemeKey,entry,
                enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,out enemy.DockPassageTarget);
        }

        /// <summary>复用普通导航走到固定落脚点；冻结时停步，完成帧仍不攻击，结束后不再恢复无敌。</summary>
        private static bool TickDockPassageEntry(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,float seconds)
        {
            if(!enemy.DockPassageEntering)return false;
            CancelMeleeWindup(enemy,true);
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds)||!RoundContentActive(battle)||!RoundPlayersReady(battle)||enemy.HerdImpact.Active||
                enemy.ContentStunUntil>battle.ContentClock.Elapsed||SlayerMartStormSpeed(battle,enemy)==0)return true;
            float distance=MathF.Sqrt(DistanceSquared(enemy.PositionX,enemy.PositionZ,enemy.DockPassageTarget.X,enemy.DockPassageTarget.Y));
            float movement=seconds*EnemySpeedMultiplier(battle)*SlayerMartStormSpeed(battle,enemy);
            if(enemy.MoveSpeed>0)movement=Math.Min(movement,distance/enemy.MoveSpeed);
            var arena=ResolveBattleArena(battle);
            MoveEnemy(enemy,enemy.DockPassageTarget.X,enemy.DockPassageTarget.Y,movement,ServerConfig.Instance.ArcadeRule,
                EffectiveObstacles(battle),IslandFor(battle),arena.EnemySeaApproachDistance,battle);
            if(DistanceSquared(enemy.PositionX,enemy.PositionZ,enemy.DockPassageTarget.X,enemy.DockPassageTarget.Y)<=.000001f)
                enemy.DockPassageEntering=false;
            return true;
        }
    }
}

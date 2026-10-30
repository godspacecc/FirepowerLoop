using FirepowerLoop.Arcade;
using UnityEngine;
using System.Linq;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>只接管普通通道敌人，特殊登场、Boss及海岛/码头保持现有事务。</summary>
        private void InitializeOutsideEntry(ArcadeCombatRuntime runtime,ArcadeEnemyRuntime enemy)
        {
            var layout=RoundLayout(runtime);
            if(ArcadeOutsideRouteRules.HasSpecialArrival(enemy.ContentSpecialKind,enemy.StageEnemy.Phase,
                enemy.ContentBossKind>0||enemy.IsBossPresentation||!string.IsNullOrEmpty(enemy.BossId),
                enemy.DockSeaEntry.Started,arena.Opening.Enabled&&!runtime.IsChallenge&&!runtime.IsFinalBoss))return;
            if(layout?.ThemeKey=="docks"&&ArcadeDockSeaEntryRules.TryStart(layout.ThemeKey,runtime.IslandEntryBatch%layout.Entries.Count,runtime.ContentSpawnSequence,out enemy.DockSeaEntry))
            {var p=ArcadeDockSeaEntryRules.Position(enemy.DockSeaEntry);enemy.Position=new Vector2(p.X,p.Y);return;}
            bool started=layout!=null&&ArcadeOutsideRouteRules.TryStart(layout,runtime.IslandEntryBatch%layout.Entries.Count,
                ArenaHalfWidth,ArenaHalfHeight,catalog.Rule.EnemyContactRadius,out enemy.OutsideEntry);
            if(!started)started=TryOutsideFallback(runtime,enemy.Position,out enemy.OutsideEntry);
            if(!started)throw new System.InvalidOperationException("敌人入口没有合法场外通道，请检查地图配置");
            enemy.Position=new Vector2(enemy.OutsideEntry.StartX,enemy.OutsideEntry.StartZ);
        }
        /// <summary>兼容旧顺序和挑战的实际场地，候选段必须避开本模式真实障碍。</summary>
        private bool TryOutsideFallback(ArcadeCombatRuntime runtime,Vector2 preferred,out ArcadeOutsideEntryState state)
        {
            var tuning=catalog.RoundLayouts.FirstOrDefault(p=>p.ArenaConfigNumber==arena.ArenaNumber);
            var obstacles=ObstaclesFor(runtime).Select(o=>new ArcadeLayoutObstacle(o.Center.x,o.Center.y,o.HalfExtents.x,o.HalfExtents.y,o.BlocksProjectiles)).ToArray();
            return ArcadeOutsideRouteRules.TryStartFallback(tuning,preferred.x,preferred.y,ArenaHalfWidth,ArenaHalfHeight,
                catalog.Rule.EnemyContactRadius,obstacles,out state);
        }
        /// <summary>无布局的兼容场次在现场交接前检查通路，禁止打到一半才发现入口无效。</summary>
        private bool ValidateOutsideFallback(ArcadeCombatRuntime runtime)=>RoundLayout(runtime)!=null||
            arena.Opening.Enabled&&!runtime.IsChallenge||TryOutsideFallback(runtime,Vector2.up*ArenaHalfHeight,out _);
        /// <summary>冻结控制只停止推进，不交给场内导航；抵达帧也由入场接管。</summary>
        private bool TickOutsideEntry(ArcadeCombatRuntime runtime,ArcadeEnemyRuntime enemy,float seconds)
        {
            if(!enemy.OutsideEntry.Active)return false;
            CancelMeleeWindup(enemy,true);
            bool frozen=enemy.HerdImpact.Active||enemy.ContentStunUntil>runtime.ContentClock.Elapsed||SlayerMartStormSpeed(runtime,enemy)==0;
            if(!ArcadeOutsideEntryRules.Advance(ref enemy.OutsideEntry,frozen?0:seconds,
                enemy.Speed*EnemySpeedMultiplier(runtime),out var position))return false;
            enemy.Position=new Vector2(position.X,position.Y);return true;
        }
        /// <summary>场外击杀奖励落在已验证入口内，玩家无需跨越边界拾取。</summary>
        private Vector2 OutsideEntryDropPosition(ArcadeEnemyRuntime enemy)=>enemy.OutsideEntry.Active
            ?new Vector2(enemy.OutsideEntry.TargetX,enemy.OutsideEntry.TargetZ):enemy.Position;
    }
}

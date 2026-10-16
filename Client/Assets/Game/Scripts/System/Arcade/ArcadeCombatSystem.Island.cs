using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>当前场地不可变轮廓；离场清空，独立房间与挑战不使用。</summary>
        private ArcadeIslandRegion islandRegion;
        private int islandRegionRound = -1;
        private readonly List<ArcadeRegionPoint> islandMovementPath = new();
        private ArcadeIslandRegion IslandFor(ArcadeCombatRuntime runtime)
        {
            if (runtime.IsChallenge || runtime.InTreasureRoom || !arena.Opening.Enabled) return null;
            int round = UsesIslandRounds(runtime) ? runtime.WaveIndex + 1 : 0;
            if (islandRegion == null || islandRegionRound != round)
            {
                islandRegionRound = round;
                islandRegion = round > 0 ? new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, round)
                    : new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight);
            }
            return islandRegion;
        }

        private Vector2 IslandPosition(ArcadeCombatRuntime runtime, Vector2 position, float radius)
        {
            var region = IslandFor(runtime);
            if (region == null) return position;
            var p = region.Constrain(position.x, position.y, radius); return new Vector2(p.X, p.Y);
        }

        /// <summary>海岛冲刺逐真实路径段消灭普通敌人；统一死亡入口保证积分/掉落仅一次。</summary>
        private void KillAlongIslandDash(ArcadeCombatRuntime runtime)
        {
            if (runtime.DashRemaining <= 0 || IslandFor(runtime) == null) return;
            float radius = catalog.Rule.PlayerRadius + catalog.Rule.EnemyContactRadius;
            foreach (var enemy in runtime.Enemies.Values.ToArray())
            {
                if (!string.IsNullOrEmpty(enemy.BossId) || enemy.IsBossPresentation ||
                    !IslandFor(runtime).Contains(enemy.Position.x, enemy.Position.y)) continue;
                for (int i = 1; i < islandMovementPath.Count; i++)
                {
                    var a = islandMovementPath[i - 1]; var b = islandMovementPath[i];
                    if (DistanceToSegmentSquared(enemy.Position, new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) > radius * radius) continue;
                    KillEnemy(runtime, enemy); break;
                }
            }
        }

        private void AwardIslandKill(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy)
        {
            if (IslandFor(runtime) == null || !string.IsNullOrEmpty(enemy.BossId) || enemy.IsBossPresentation) return;
            var model = this.GetModel<ArcadeRunModel>(); var run = model.Current;
            var rules = catalog.TreasureRooms.FirstOrDefault();
            if (run != null && ArcadeTreasureScoreRules.TryAwardKill(
                new ArcadeTreasureScoreState(run.Score, run.ScoreMultiplier, run.MultiplierProgress, run.RemainingLives),
                rules, out var next)) model.SetTransientTreasureScore(next);
        }
    }
}

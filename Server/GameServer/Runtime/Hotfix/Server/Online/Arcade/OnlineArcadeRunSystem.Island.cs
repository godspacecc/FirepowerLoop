using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        private static ArcadeIslandRegion IslandFor(OnlineArcadeBattleRuntime battle)
        {
            if (battle.IslandRegionResolved) return battle.IslandRegion;
            battle.IslandRegionResolved = true;
            if (!battle.IsChallenge && ArcadeOpeningParameters.FromConfig(ResolveBattleArena(battle)).Enabled)
                battle.IslandRegion = new ArcadeIslandRegion(ServerConfig.Instance.ArcadeRule.ArenaHalfWidth,
                    ServerConfig.Instance.ArcadeRule.ArenaHalfHeight);
            return battle.IslandRegion;
        }

        /// <summary>冲刺只使用边界限制后的每段真实轨迹，统一死亡入口提交个人积分与掉落。</summary>
        private static void KillAlongIslandDash(OnlineArcadeRunActor actor, OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player)
        {
            if (player.DashSeconds <= 0 || player.InTreasureRoom || IslandFor(battle) == null) return;
            float radius = ServerConfig.Instance.ArcadeRule.PlayerRadius + ServerConfig.Instance.ArcadeRule.EnemyContactRadius;
            foreach (var enemy in battle.Enemies.Values.ToArray())
            {
                if (!string.IsNullOrEmpty(enemy.BossId) || !IslandFor(battle).Contains(enemy.PositionX, enemy.PositionZ)) continue;
                for (int i = 1; i < battle.IslandMovementPath.Count; i++)
                {
                    var a = battle.IslandMovementPath[i - 1]; var b = battle.IslandMovementPath[i];
                    float dx = b.X - a.X, dy = b.Y - a.Y, length2 = dx * dx + dy * dy;
                    float t = length2 > 0 ? Math.Clamp(((enemy.PositionX - a.X) * dx + (enemy.PositionZ - a.Y) * dy) / length2, 0f, 1f) : 0;
                    float x = a.X + dx * t, y = a.Y + dy * t;
                    if (DistanceSquared(enemy.PositionX, enemy.PositionZ, x, y) > radius * radius) continue;
                    enemy.LastDamagePlayerId = player.PlayerId;
                    actor.KillEnemy(battle, enemy); break;
                }
            }
        }

        private static void AwardIslandKill(OnlineArcadeRunActor actor, OnlineArcadeBattleRuntime battle, OnlineArcadeEnemyRuntime enemy)
        {
            if (IslandFor(battle) == null || !string.IsNullOrEmpty(enemy.BossId) || enemy.LastDamagePlayerId == 0) return;
            var player = actor.Record.Players.FirstOrDefault(p => p.PlayerId == enemy.LastDamagePlayerId);
            var config = ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.FirstOrDefault();
            if (player == null || config == null) return;
            var rules = ArcadeTreasureRoomParameters.FromConfig(config);
            if (ArcadeTreasureScoreRules.TryAwardKill(new ArcadeTreasureScoreState(player.Score, player.ScoreMultiplier,
                player.MultiplierProgress, player.Lives), rules, out var next))
            { player.Score = next.Score; player.Lives = next.Lives; }
        }
    }
}

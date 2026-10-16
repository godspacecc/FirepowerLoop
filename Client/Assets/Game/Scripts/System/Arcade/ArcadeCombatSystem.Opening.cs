using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>规则只在新休闲前两场的普通战斗生效，不影响挑战和独立 Boss。</summary>
        private bool UsesEarlyRounds(ArcadeCombatRuntime runtime) => !runtime.IsChallenge && !runtime.IsFinalBoss &&
            ArcadeOpeningRules.UsesEarlyRounds(this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true, arena.ArenaNumber);

        /// <summary>首场沿用原休整；新休闲分支读取同一时限，水岸参数仍只属于海岛。</summary>
        private bool UsesEarlyRest(ArcadeCombatRuntime runtime) => !runtime.IsChallenge && !runtime.IsFinalBoss &&
            arena.Opening.HasRest && (arena.Opening.Enabled || UsesEarlyRounds(runtime));

        /// <summary>第四轮活动战斗按配置结算营火持续伤害，沿用复活、冲刺、护盾和无敌保护。</summary>
        private void TickOpeningFire(ArcadeCombatRuntime runtime, float seconds)
        {
            var p = arena.Opening;
            if (UsesIslandRounds(runtime) && runtime.IslandCampfireWarningRemaining > 0)
            {
                runtime.IslandCampfireWarningRemaining = Mathf.Max(0, runtime.IslandCampfireWarningRemaining - seconds);
                return;
            }
            if (!ArcadeOpeningRules.CampfireActive(p, runtime.WaveIndex, runtime.IsChallenge || runtime.IsFinalBoss,
                runtime.WaveClearAwarded, runtime.AwaitingExit, runtime.InTreasureRoom, runtime.RespawnRemaining > 0)) return;
            runtime.CampfireCooldown = Mathf.Max(0, runtime.CampfireCooldown - seconds);
            if (runtime.CampfireCooldown > 0 || !p.TouchesCampfire(runtime.PlayerPosition.x,
                runtime.PlayerPosition.y, catalog.Rule.PlayerRadius)) return;
            runtime.CampfireCooldown = p.CampfireIntervalSeconds;
            DamagePlayer(runtime, p.CampfireDamage);
        }

        /// <summary>普通财宝复用同一积分合同；仅更新本场工作态，不同时发购物现金或新增安全节点。</summary>
        private bool AwardOrdinaryTreasure(int points)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var run = model.Current;
            var rules = catalog.TreasureRooms.FirstOrDefault();
            if (run == null || rules == null) return false;
            var reward = new ArcadeTreasureRewardParameters(0, FirepowerLoop.Arcade.ArcadeTreasureRewardKind.GoldBar, 0, 0, points, points, 1);
            if (!ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(run.Score, run.ScoreMultiplier,
                run.MultiplierProgress, run.RemainingLives), reward, rules, out var next)) return false;
            model.SetTransientTreasureScore(next);
            return true;
        }
    }
}

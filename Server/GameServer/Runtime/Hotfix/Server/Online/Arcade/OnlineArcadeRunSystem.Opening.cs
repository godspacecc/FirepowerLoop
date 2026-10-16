using System;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>以本局模式及地图候选限定前期迁移，不给特殊阶段套用普通轮规则。</summary>
        private static bool UsesEarlyRounds(OnlineArcadeBattleRuntime battle) => !battle.IsChallenge && !battle.IsFinalBoss &&
            ArcadeOpeningRules.UsesEarlyRounds(battle.IsCasualRoute, battle.ArenaConfigNumber);

        private static bool UsesEarlyRest(OnlineArcadeBattleRuntime battle)
        {
            var p = ArcadeOpeningParameters.FromConfig(ResolveBattleArena(battle));
            return !battle.IsChallenge && !battle.IsFinalBoss && p.HasRest && (p.Enabled || UsesEarlyRounds(battle));
        }

        /// <summary>第四轮权威营火只造成配置连续伤害；不绕过护盾、冲刺或复活保护。</summary>
        private static bool TickOpeningFire(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle, float seconds)
        {
            var p = ArcadeOpeningParameters.FromConfig(ResolveBattleArena(battle));
            if (!ArcadeOpeningRules.CampfireActive(p, battle.WaveIndex, battle.IsChallenge || battle.IsFinalBoss,
                battle.WaveClearAwarded, battle.AwaitingExit, false, false)) return false;
            bool changed = false;
            foreach (var player in battle.Players.Values)
            {
                if (!player.Alive || player.InTreasureRoom || player.RespawnSeconds > 0) continue;
                player.CampfireCooldown = Math.Max(0, player.CampfireCooldown - seconds);
                if (player.CampfireCooldown > 0 || !p.TouchesCampfire(player.PositionX, player.PositionZ,
                    ServerConfig.Instance.ArcadeRule.PlayerRadius)) continue;
                player.CampfireCooldown = p.CampfireIntervalSeconds;
                self.DamagePlayer(battle, player, p.CampfireDamage);
                changed = true;
            }
            return changed;
        }
    }
}

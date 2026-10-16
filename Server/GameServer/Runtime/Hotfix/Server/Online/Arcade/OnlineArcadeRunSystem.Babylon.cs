using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        private static bool UsesBabylon(OnlineArcadeBattleRuntime b) => ArcadeBabylonRules.Enabled(b.IsCasualRoute, b.ArenaConfigNumber, b.IsChallenge);
        private static void InitializeCrates(OnlineArcadeBattleRuntime b)
        {
            if (b.CratesInitialized) return;
            b.CratesInitialized = true;
            if (!UsesBabylon(b)) return;
            foreach (var c in ArcadeBabylonParameters.FromConfig(ResolveBattleArena(b)).Crates)
                b.Crates.Add(c.Id, new ArcadeCrateState(c));
        }
        private static void DamageEnemy(OnlineArcadeBattleRuntime battle, OnlineArcadeEnemyRuntime enemy, int damage)
        {
            if(damage<=0)return;
            if(battle.RoundLayout!=null)
            {
                var p=ContentTuning(battle);
                if(enemy.SpecialKind==3)
                {
                    if(enemy.ContentMannequinHits>0&&enemy.ContentElapsed-enemy.LastContentHitAt<p.SkillWarning)return;
                    enemy.LastContentHitAt=enemy.ContentElapsed;enemy.ContentMannequinHits++;
                    enemy.CurrentHealth=enemy.ContentMannequinHits>=3?0:Math.Max(1,(int)Math.Ceiling(enemy.MaxHealth*(3-enemy.ContentMannequinHits)/3d));return;
                }
                if(enemy.SpecialKind==13&&enemy.ContentElapsed<p.SkillWarning)return;
            }
            float scale = battle.RoundLayout==null ? 1 : FirepowerLoop.Arcade.ArcadeRoundContentRules.IncomingDamageScale(enemy.SpecialKind,enemy.ContentElapsed,ContentTuning(battle));
            if(scale<=0)return;
            enemy.LastContentHitAt=enemy.ContentElapsed;
            enemy.CurrentHealth -= ArcadeBabylonRules.Absorb(ref enemy.ArmorHealth, Math.Max(1,(int)MathF.Round(damage*scale)));
        }
        /// <summary>服务器创建唯一共享掉落，命中者不直接获积分；预算不足保持箱子待领取。</summary>
        private static bool DamageCrate(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime b, int id, int damage)
        {
            if (!b.Crates.TryGetValue(id, out var c)) return false;
            var pool = ServerConfig.Instance.Tables.TbArcadeDrop.DataList.Where(d => d.Weight > 0 && d.MinArena <= 2 &&
                (d.MaxArena == 0 || d.MaxArena >= 2) && (d.Kind == ArcadeDropKind.RunGold || d.Kind == ArcadeDropKind.Bomb ||
                d.Kind == ArcadeDropKind.Dash || d.Kind == ArcadeDropKind.TemporaryWeapon || d.ProductId == "arcade.powerup.shield" ||
                d.ProductId == "arcade.powerup.boots")).OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
            if (!c.Damage(damage, b.Drops.Count < ServerConfig.Instance.ArcadeRule.MaxDrops && pool.Length > 0)) return true;
            b.Crates.Remove(id);b.DestroyedCrateIds.Add(id);
            int weight = pool.Sum(d => d.Weight * (d.Kind == ArcadeDropKind.RunGold ? ResolveBattleArena(b).CrateTreasureWeightMultiplier : 1));
            int roll = (int)(MixDropSeed(unchecked((ulong)self.Record.RunId), b.ArenaNumber, id + 100000) % (uint)weight);
            foreach (var d in pool)
            {
                int w = d.Weight * (d.Kind == ArcadeDropKind.RunGold ? ResolveBattleArena(b).CrateTreasureWeightMultiplier : 1);
                if (roll >= w) { roll -= w; continue; }
                long runtimeId = b.NextDropRuntimeId++;
                b.Drops.Add(runtimeId, new OnlineArcadeDropRuntime { RuntimeId = runtimeId, DropConfigId = d.Id,
                    PositionX = c.Definition.X, PositionZ = c.Definition.Y }); break;
            }
            return true;
        }
        private static bool HitCrate(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime b,
            float ax, float az, float bx, float bz, int damage)
        {
            foreach (int id in b.Crates.Keys.OrderBy(id => id).ToArray())
            {
                var c = b.Crates[id].Definition;
                if (ArcadeBabylonRules.Intersects(ax, az, bx, bz, c.X, c.Y, c.Radius + ProjectileHitRadius))
                    return self.DamageCrate(b, id, damage);
            }
            return false;
        }
        private static void DamageCratesInRadius(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime b,
            float x, float z, float radius, int damage)
        {
            foreach (int id in b.Crates.Keys.ToArray())
            {
                var c = b.Crates[id].Definition;
                if (DistanceSquared(x, z, c.X, c.Y) <= (radius + c.Radius) * (radius + c.Radius)) self.DamageCrate(b, id, damage);
            }
        }
    }
}

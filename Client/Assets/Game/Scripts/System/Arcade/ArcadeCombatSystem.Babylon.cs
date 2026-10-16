using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        private bool UsesBabylon(ArcadeCombatRuntime r) => ArcadeBabylonRules.Enabled(
            this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true, arena.ArenaNumber, r.IsChallenge);
        /// <summary>本场首次建队列时建立箱子；切轮不刷新，回滚后的新现场重新建立。</summary>
        private void InitializeCrates(ArcadeCombatRuntime r)
        {
            if (r.CratesInitialized) return;
            r.CratesInitialized = true;
            if (!UsesBabylon(r) || arena.Babylon == null) return;
            foreach (var c in arena.Babylon.Crates) r.Crates.Add(c.Id, new ArcadeCrateState(c));
        }
        /// <summary>护甲只在配置轮次新生成时赋予；所有普通伤害共用吸收入口。</summary>
        private void AssignArmor(ArcadeCombatRuntime r, ArcadeEnemyRuntime enemy)
        {
            if (UsesBabylon(r) && arena.Babylon?.ArmorWaveNumber == r.WaveIndex + 1)
                enemy.ArmorHealth = arena.Babylon.ArmorHealth;
        }
        /// <summary>先执行特殊受击状态，再读取生命扣除护甲后的伤害，避免复合赋值覆盖人偶阶段。</summary>
        private void DamageEnemy(ArcadeEnemyRuntime enemy, int damage)
        {
            int resolved = RoundIncomingDamage(enemy, damage);
            enemy.Health -= ArcadeBabylonRules.Absorb(ref enemy.ArmorHealth, resolved);
        }

        private ArcadeDropDefinition[] CrateDropPool() => catalog.Drops.Where(d => d.Weight > 0 &&
            IsDropEligible(d, 2) && (d.Kind == global::ArcadeDropKind.RunGold || d.Kind == global::ArcadeDropKind.Bomb ||
            d.Kind == global::ArcadeDropKind.Dash || d.Kind == global::ArcadeDropKind.TemporaryWeapon ||
            d.ProductId == "arcade.powerup.shield" || d.ProductId == "arcade.powerup.boots"))
            .OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        /// <summary>破坏成功后先移除箱身份，再创建唯一掉落；预算不足不消耗最后一击。</summary>
        private bool DamageCrate(ArcadeCombatRuntime r, int id, int damage)
        {
            if (!r.Crates.TryGetValue(id, out var crate)) return false;
            var candidates = CrateDropPool();
            if (!crate.Damage(damage, r.Drops.Count < catalog.Rule.MaxDrops && candidates.Length > 0)) return true;
            r.Crates.Remove(id);
            int roll = random.Next(candidates.Sum(d => d.Weight * (d.Kind == global::ArcadeDropKind.RunGold ? arena.Babylon.CrateTreasureWeightMultiplier : 1)));
            foreach (var d in candidates)
            {
                int weight = d.Weight * (d.Kind == global::ArcadeDropKind.RunGold ? arena.Babylon.CrateTreasureWeightMultiplier : 1);
                if (roll >= weight) { roll -= weight; continue; }
                var drop = runtimePool.RentDrop(); drop.Id = r.NextEntityId++; drop.Definition = d;
                drop.Position = new Vector2(crate.Definition.X, crate.Definition.Y); r.Drops.Add(drop.Id, drop); break;
            }
            return true;
        }
        /// <summary>玩家弹丸线段命中，避免高速子弹穿过小箱；敌方弹丸不打开箱子。</summary>
        private bool HitCrate(ArcadeCombatRuntime r, Vector2 from, Vector2 to, int damage)
        {
            foreach (var id in r.Crates.Keys.OrderBy(id => id).ToArray())
            {
                var c = r.Crates[id].Definition;
                if (!ArcadeBabylonRules.Intersects(from.x, from.y, to.x, to.y, c.X, c.Y, c.Radius + ProjectileHitRadius)) continue;
                return DamageCrate(r, id, damage);
            }
            return false;
        }
        private void DamageCratesInRadius(ArcadeCombatRuntime r, Vector2 center, float radius, int damage)
        {
            foreach (var id in r.Crates.Keys.ToArray())
            {
                var c = r.Crates[id].Definition;
                if (Vector2.Distance(center, new Vector2(c.X, c.Y)) <= radius + c.Radius) DamageCrate(r, id, damage);
            }
        }
    }
}

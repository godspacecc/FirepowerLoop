using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>为码头原队列登记入场状态，海路沿既有动作、木通道沿既有行走。</summary>
        private void InitializeDockSeaEntry(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy)
        {
            var layout = RoundLayout(runtime);
            if (layout == null || runtime.IsChallenge || runtime.IsFinalBoss || enemy.ContentBossKind > 0) return;
            int passage = runtime.IslandEntryBatch % layout.Entries.Count;
            enemy.DockPassageEntering = ArcadeDockSeaEntryRules.TryGetDeckEntryTarget(layout.ThemeKey, passage,
                enemy.Position.x, enemy.Position.y, catalog.Rule.EnemyContactRadius, out enemy.DockPassageTarget);
            if (!ArcadeDockSeaEntryRules.TryStart(layout.ThemeKey, passage, runtime.ContentSpawnSequence - 1, out enemy.DockSeaEntry)) return;
            enemy.Position = new Vector2(ArcadeDockSeaEntryRules.Sample(0).X, enemy.DockSeaEntry.Lane);
        }
        /// <summary>先走完整个通道再参战；玩家堵在入口不会令敌人停在免疫区攻击。</summary>
        private bool TickDockPassageEntry(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, float seconds)
        {
            if (!enemy.DockPassageEntering) return false;
            CancelMeleeWindup(enemy, true);
            if (!RoundContentActive(runtime) || !ArcadeDockSeaEntryRules.Finite(seconds) || seconds <= 0 || enemy.HerdImpact.Active ||
                enemy.ContentStunUntil > runtime.ContentClock.Elapsed || SlayerMartStormSpeed(runtime, enemy) == 0) return true;
            var target = new Vector2(enemy.DockPassageTarget.X, enemy.DockPassageTarget.Y);
            float distance = Vector2.Distance(enemy.Position, target);
            float movement = seconds * EnemySpeedMultiplier(runtime) * SlayerMartStormSpeed(runtime, enemy);
            MoveEnemyTowards(runtime, enemy, target, Mathf.Min(movement, distance / Mathf.Max(.1f, enemy.Speed)));
            if (Vector2.Distance(enemy.Position, target) <= .001f) enemy.DockPassageEntering = false;
            return true;
        }

        /// <summary>进场不追击/攻击；受控保持姿态，登台前免受攻击，登台后沿原伤害路径结算。</summary>
        private bool TickDockSeaEntry(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, float seconds)
        {
            if (!enemy.DockSeaEntry.Active) return false;
            CancelMeleeWindup(enemy, true);
            bool frozen = enemy.HerdImpact.Active || enemy.ContentStunUntil > runtime.ContentClock.Elapsed || SlayerMartStormSpeed(runtime, enemy) == 0;
            ArcadeDockSeaEntryRules.Advance(ref enemy.DockSeaEntry, frozen ? 0 : seconds);
            enemy.Position = new Vector2(ArcadeDockSeaEntryRules.Sample(enemy.DockSeaEntry.Age).X, enemy.DockSeaEntry.Lane);
            return true;
        }
    }
}

using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>只改变原右侧通道的既有出生，原队列、属性和载桶分配不变。</summary>
        private void InitializeDockSeaEntry(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy)
        {
            var layout = RoundLayout(runtime);
            if (layout == null || runtime.IsFinalBoss || !ArcadeDockSeaEntryRules.TryStart(layout.ThemeKey,
                runtime.IslandEntryBatch % layout.Entries.Count, runtime.ContentSpawnSequence - 1, out enemy.DockSeaEntry)) return;
            enemy.Position = new Vector2(ArcadeDockSeaEntryRules.Sample(0).X, enemy.DockSeaEntry.Lane);
        }
        /// <summary>进场不追击/攻击；受控保持姿态，仍可沿原生命和伤害路径击杀。</summary>
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

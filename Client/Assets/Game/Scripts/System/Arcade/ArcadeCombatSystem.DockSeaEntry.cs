using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>为码头原队列登记入场状态，三路沿已采用海中攀爬动作。</summary>
        private void InitializeDockSeaEntry(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy)
        {
            var layout = RoundLayout(runtime);
            if (layout == null || runtime.IsChallenge || runtime.IsFinalBoss || enemy.ContentBossKind > 0) return;
            int passage = runtime.IslandEntryBatch % layout.Entries.Count;
            if (!ArcadeDockSeaEntryRules.TryStart(layout.ThemeKey, passage, runtime.ContentSpawnSequence - 1, out enemy.DockSeaEntry)) return;
            var start = ArcadeDockSeaEntryRules.Position(enemy.DockSeaEntry);
            enemy.Position = new Vector2(start.X, start.Y);
        }
        /// <summary>进场不追击/攻击；受控保持姿态，登台前免受攻击，登台后沿原伤害路径结算。</summary>
        private bool TickDockSeaEntry(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, float seconds)
        {
            if (!enemy.DockSeaEntry.Active) return false;
            CancelMeleeWindup(enemy, true);
            bool frozen = enemy.HerdImpact.Active || enemy.ContentStunUntil > runtime.ContentClock.Elapsed || SlayerMartStormSpeed(runtime, enemy) == 0;
            ArcadeDockSeaEntryRules.Advance(ref enemy.DockSeaEntry, frozen ? 0 : seconds);
            var position = ArcadeDockSeaEntryRules.Position(enemy.DockSeaEntry);
            enemy.Position = new Vector2(position.X, position.Y);
            return true;
        }
    }
}

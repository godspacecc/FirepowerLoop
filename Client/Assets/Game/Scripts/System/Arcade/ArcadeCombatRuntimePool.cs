using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 复用街机瞬时规则对象；容量严格受当前配置预算约束，不保存玩法状态，也不拥有任何 Unity 场景对象。
    /// </summary>
    internal sealed class ArcadeCombatRuntimePool
    {
        private readonly Stack<ArcadeEnemyRuntime> enemies = new Stack<ArcadeEnemyRuntime>();
        private readonly Stack<ArcadeProjectileRuntime> projectiles = new Stack<ArcadeProjectileRuntime>();
        private readonly Stack<ArcadeDropRuntime> drops = new Stack<ArcadeDropRuntime>();
        private int enemyCapacity;
        private int projectileCapacity;
        private int dropCapacity;

        internal int EnemyCount => enemies.Count;
        internal int ProjectileCount => projectiles.Count;
        internal int DropCount => drops.Count;

        /// <summary>每次进入竞技场时按配置刷新并预热容量；降低预算会立即裁掉多余缓存。</summary>
        internal void Configure(int maxEnemies, int maxProjectiles, int maxDrops)
        {
            enemyCapacity = System.Math.Max(0, maxEnemies);
            projectileCapacity = System.Math.Max(0, maxProjectiles);
            dropCapacity = System.Math.Max(0, maxDrops);
            Trim(enemies, enemyCapacity);
            Trim(projectiles, projectileCapacity);
            Trim(drops, dropCapacity);
            while (enemies.Count < enemyCapacity) enemies.Push(new ArcadeEnemyRuntime());
            while (projectiles.Count < projectileCapacity) projectiles.Push(new ArcadeProjectileRuntime());
            while (drops.Count < dropCapacity) drops.Push(new ArcadeDropRuntime());
        }

        internal ArcadeEnemyRuntime RentEnemy() => enemies.Count > 0 ? enemies.Pop() : new ArcadeEnemyRuntime();
        internal ArcadeProjectileRuntime RentProjectile() => projectiles.Count > 0 ? projectiles.Pop() : new ArcadeProjectileRuntime();
        internal ArcadeDropRuntime RentDrop() => drops.Count > 0 ? drops.Pop() : new ArcadeDropRuntime();

        internal void Return(ArcadeEnemyRuntime value)
        {
            if (value == null) return;
            value.Reset();
            if (enemies.Count < enemyCapacity) enemies.Push(value);
        }

        internal void Return(ArcadeProjectileRuntime value)
        {
            if (value == null) return;
            value.Reset();
            if (projectiles.Count < projectileCapacity) projectiles.Push(value);
        }

        internal void Return(ArcadeDropRuntime value)
        {
            if (value == null) return;
            value.Reset();
            if (drops.Count < dropCapacity) drops.Push(value);
        }

        internal void Clear()
        {
            enemies.Clear();
            projectiles.Clear();
            drops.Clear();
        }

        private static void Trim<T>(Stack<T> values, int capacity)
        {
            while (values.Count > capacity) values.Pop();
        }
    }
}

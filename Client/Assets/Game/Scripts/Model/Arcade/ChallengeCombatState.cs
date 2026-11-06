using System.Collections.Generic;

namespace Game
{
    /// <summary>独立挑战战斗权威，只保存本局生命、生成进度和敌人存活状态。</summary>
    public sealed class ChallengeCombatState
    {
        public sealed class Enemy
        {
            public int Health;
            public float AttackRemaining;
        }
        public readonly Dictionary<int, Enemy> Enemies = new();
        public int PlayerHealth;
        public int Spawned;
        public int Kills;
        public float SpawnRemaining;
        public bool Finished;

        public ChallengeCombatState(int health) { PlayerHealth = health; }

        /// <summary>死亡只提交一次，重复命中不会重复增加击杀。</summary>
        public bool DamageEnemy(int id, int damage)
        {
            if (Finished || damage <= 0 || !Enemies.TryGetValue(id, out var enemy)) return false;
            enemy.Health = System.Math.Max(0, enemy.Health - damage);
            if (enemy.Health > 0) return false;
            Enemies.Remove(id);
            Kills++;
            return true;
        }
    }
}

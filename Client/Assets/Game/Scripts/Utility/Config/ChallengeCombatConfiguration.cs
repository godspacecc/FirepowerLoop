using System;

namespace Game
{
    /// <summary>经典挑战独立战斗参数快照，所有数值来自本模式权威表。</summary>
    public sealed class ChallengeCombatConfiguration
    {
        /// <summary>独立挑战参数：EnemyPrefabAddress。</summary>
        public string EnemyPrefabAddress { get; }
        /// <summary>独立挑战参数：EnemyHealth。</summary>
        public int EnemyHealth { get; }
        /// <summary>独立挑战参数：EnemySpeed。</summary>
        public float EnemySpeed { get; }
        /// <summary>独立挑战参数：ContactDamage。</summary>
        public int ContactDamage { get; }
        /// <summary>独立挑战参数：ContactInterval。</summary>
        public float ContactInterval { get; }
        /// <summary>独立挑战参数：PlayerHealth。</summary>
        public int PlayerHealth { get; }
        /// <summary>独立挑战参数：RifleDamage。</summary>
        public int RifleDamage { get; }
        /// <summary>独立挑战参数：PistolDamage。</summary>
        public int PistolDamage { get; }
        /// <summary>独立挑战参数：Range。</summary>
        public float Range { get; }
        /// <summary>独立挑战参数：SpawnInterval。</summary>
        public float SpawnInterval { get; }
        /// <summary>独立挑战参数：EnemyCount。</summary>
        public int EnemyCount { get; }
        /// <summary>复制唯一权威配置行。</summary>
        public ChallengeCombatConfiguration(Tables tables) : this(GetRow(tables)) {}
        private ChallengeCombatConfiguration(ChallengeCombatConfig row) : this(row.EnemyPrefabAddress, row.EnemyHealth, row.EnemySpeed, row.ContactDamage, row.ContactInterval, row.PlayerHealth, row.RifleDamage, row.PistolDamage, row.Range, row.SpawnInterval, row.EnemyCount) {}
        /// <summary>建立已校验快照，非法参数明确失败。</summary>
        public ChallengeCombatConfiguration(string enemyPrefabAddress, int enemyHealth, float enemySpeed, int contactDamage, float contactInterval, int playerHealth, int rifleDamage, int pistolDamage, float range, float spawnInterval, int enemyCount)
        {
            if(string.IsNullOrWhiteSpace(enemyPrefabAddress)) throw new ArgumentException("挑战敌人地址为空");
            if(enemyHealth <= 0) throw new ArgumentException("挑战参数无效：EnemyHealth");
            if(enemySpeed <= 0 || float.IsNaN(enemySpeed) || float.IsInfinity(enemySpeed)) throw new ArgumentException("挑战参数无效：EnemySpeed");
            if(contactDamage <= 0) throw new ArgumentException("挑战参数无效：ContactDamage");
            if(contactInterval <= 0 || float.IsNaN(contactInterval) || float.IsInfinity(contactInterval)) throw new ArgumentException("挑战参数无效：ContactInterval");
            if(playerHealth <= 0) throw new ArgumentException("挑战参数无效：PlayerHealth");
            if(rifleDamage <= 0) throw new ArgumentException("挑战参数无效：RifleDamage");
            if(pistolDamage <= 0) throw new ArgumentException("挑战参数无效：PistolDamage");
            if(range <= 0 || float.IsNaN(range) || float.IsInfinity(range)) throw new ArgumentException("挑战参数无效：Range");
            if(spawnInterval <= 0 || float.IsNaN(spawnInterval) || float.IsInfinity(spawnInterval)) throw new ArgumentException("挑战参数无效：SpawnInterval");
            if(enemyCount <= 0) throw new ArgumentException("挑战参数无效：EnemyCount");
            EnemyPrefabAddress=enemyPrefabAddress;
            EnemyHealth=enemyHealth;
            EnemySpeed=enemySpeed;
            ContactDamage=contactDamage;
            ContactInterval=contactInterval;
            PlayerHealth=playerHealth;
            RifleDamage=rifleDamage;
            PistolDamage=pistolDamage;
            Range=range;
            SpawnInterval=spawnInterval;
            EnemyCount=enemyCount;
        }
        /// <summary>避免多行或缺表时静默采用不确定配置。</summary>
        private static ChallengeCombatConfig GetRow(Tables tables)
        {
            var rows=tables?.TbChallengeCombat?.DataList;
            if(rows==null || rows.Count!=1 || rows[0].Id!="challenge.combat")
                throw new ArgumentException("挑战战斗配置必须且只能包含 challenge.combat");
            return rows[0];
        }
    }
}

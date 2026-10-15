namespace Game
{
    /// <summary>
    /// 标记战斗单位所属阵营，供伤害、死亡和目标选择规则判断。
    /// </summary>
    public enum CombatFaction
    {
        Player,
        Enemy
    }

    /// <summary>
    /// 表示单个战斗单位的生命、攻击与目标快照，不包含掉落或表现数据。
    /// </summary>
    public readonly struct CombatantSnapshot
    {
        public CombatantSnapshot(
            int unitId,
            CombatFaction faction,
            int hp,
            int maxHP,
            int attackDamage,
            int targetUnitId,
            bool isDead)
        {
            UnitId = unitId;
            Faction = faction;
            HP = hp;
            MaxHP = maxHP;
            AttackDamage = attackDamage;
            TargetUnitId = targetUnitId;
            IsDead = isDead;
        }

        public int UnitId { get; }
        public CombatFaction Faction { get; }
        public int HP { get; }
        public int MaxHP { get; }
        public int AttackDamage { get; }
        public int TargetUnitId { get; }
        public bool IsDead { get; }
    }
}

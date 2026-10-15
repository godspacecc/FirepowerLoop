namespace Game
{
    /// <summary>玩家请求为当前远程武器手动装填。</summary>
    public readonly struct PlayerReloadRequestedEvent
    {
    }

    /// <summary>
    /// 战斗伤害请求事件，记录攻击者、目标和请求伤害值。
    /// </summary>
    public readonly struct CombatDamageRequestedEvent
    {
        public CombatDamageRequestedEvent(int attackerUnitId, int targetUnitId, int damage)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = targetUnitId;
            Damage = damage;
        }

        public int AttackerUnitId { get; }
        public int TargetUnitId { get; }
        public int Damage { get; }
    }

    /// <summary>
    /// 战斗伤害生效事件，记录攻击者、目标和实际伤害值。
    /// </summary>
    public readonly struct CombatDamageAppliedEvent
    {
        public CombatDamageAppliedEvent(int attackerUnitId, int targetUnitId, int damage)
        {
            AttackerUnitId = attackerUnitId;
            TargetUnitId = targetUnitId;
            Damage = damage;
        }

        public int AttackerUnitId { get; }
        public int TargetUnitId { get; }
        public int Damage { get; }
    }

    /// <summary>
    /// 战斗生命变化事件，统一通知玩家与敌人的表现刷新。
    /// </summary>
    public readonly struct CombatHealthChangedEvent
    {
        public CombatHealthChangedEvent(
            int unitId,
            CombatFaction faction,
            int current,
            int maximum)
        {
            UnitId = unitId;
            Faction = faction;
            Current = current;
            Maximum = maximum;
        }

        public int UnitId { get; }
        public CombatFaction Faction { get; }
        public int Current { get; }
        public int Maximum { get; }
    }

    /// <summary>
    /// 战斗单位死亡事件，只携带战斗身份，不混入掉落或表现数据。
    /// </summary>
    public readonly struct CombatantDiedEvent
    {
        public CombatantDiedEvent(
            int unitId,
            int killerUnitId)
        {
            UnitId = unitId;
            KillerUnitId = killerUnitId;
        }

        public int UnitId { get; }
        public int KillerUnitId { get; }
    }
}

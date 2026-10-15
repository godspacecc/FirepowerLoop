namespace ET.Server
{
    /// <summary>单地图玩家箭的服务器运行态，伤害与来源在接受攻击时锁定；不持有客户端对象。</summary>
    [EnableClass] public sealed class OnlineWorldProjectileRuntime
    {
        public long RuntimeId,OwnerUnitId,OwnerLifecycleRevision,AttackSequence,SpawnServerTime,SampleServerTime,StateRevision;
        public int WeaponItemId,Damage;
        public string ProjectileConfigId,RecoveryLootConfigId;
        public Game.WorldCollisionVector Position,Direction;
        public Game.WorldProjectileShape Shape;
        /// <summary>速度为米/秒，剩余射程为米；服务器实际时间推进并钳制末段距离。</summary>
        public float Speed,RemainingDistance;
    }
}

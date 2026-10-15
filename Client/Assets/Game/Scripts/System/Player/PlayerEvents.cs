namespace Game
{
    /// <summary>
    /// 玩家 Unit 已在当前世界会话提交完成的事件，只携带稳定身份。
    /// </summary>
    public readonly struct PlayerReadyEvent
    {
        public PlayerReadyEvent(int unitId, long mapInstanceId)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
    }

    /// <summary>街机地图的正式玩家表现已提交；只供相机和街机表现绑定，不注册搜打撤战斗与生存状态。</summary>
    public readonly struct ArcadePlayerReadyEvent
    {
        public ArcadePlayerReadyEvent(int unitId, long mapInstanceId)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
    }
}

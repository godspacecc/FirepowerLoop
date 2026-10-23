namespace ET.Server
{
    /// <summary>权威碰撞产生的纯值事实，不持有客户端资源或修改伤害。</summary>
    [EnableClass]
    public sealed class OnlineArcadeWeaponFeedbackRuntime
    {
        /// <summary>战斗内单调递增序列。</summary>
        public long Sequence;
        /// <summary>1为身体命中，2为障碍火星，3为弹丸爆炸。</summary>
        public int Kind;
        /// <summary>真实结算点和生成时半径，地图局部坐标。</summary>
        public float PositionX, PositionZ, Radius;
        /// <summary>碰撞前的来弹方向，仅供表面火星朝向。</summary>
        public float DirectionX, DirectionZ;
        /// <summary>轮次隔离与过期丢弃依据。</summary>
        public int RoundGeneration;
        /// <summary>实际碰撞的服务器毫秒时刻。</summary>
        public long ServerTime;
    }
}

namespace Game
{
    /// <summary>只读武器反馈事实，位置、方向及半径均由服务器产生。</summary>
    public readonly struct OnlineArcadeWeaponFeedbackSnapshot
    {
        public OnlineArcadeWeaponFeedbackSnapshot(long sequence,int kind,float x,float z,float directionX,float directionZ,float radius,int roundGeneration,long serverTime)
        {Sequence=sequence;Kind=kind;X=x;Z=z;DirectionX=directionX;DirectionZ=directionZ;Radius=radius;RoundGeneration=roundGeneration;ServerTime=serverTime;}
        /// <summary>当前轮单调序列。</summary>
        public long Sequence {get;}
        /// <summary>1身体命中、2障碍火星、3武器爆炸。</summary>
        public int Kind {get;}
        /// <summary>地图局部结算位置。</summary>
        public float X {get;}
        public float Z {get;}
        /// <summary>碰撞来向，仅控制表面火星。</summary>
        public float DirectionX {get;}
        public float DirectionZ {get;}
        /// <summary>仅供选择和缩放既有爆炸表现。</summary>
        public float Radius {get;}
        /// <summary>轮次隔离。</summary>
        public int RoundGeneration {get;}
        /// <summary>实际碰撞的服务器毫秒时刻。</summary>
        public long ServerTime {get;}
    }
}

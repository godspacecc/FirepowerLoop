namespace Game
{
    /// <summary>服务器确认的一次炸弹使用；不携带伤害值或客户端可写状态。</summary>
    public readonly struct OnlineArcadeBombBlastSnapshot
    {
        /// <summary>本场序列、使用者、使用瞬间位置与所属轮代次。</summary>
        public OnlineArcadeBombBlastSnapshot(long sequence,long playerId,float positionX,float positionZ,int roundGeneration)
        {Sequence=sequence;PlayerId=playerId;PositionX=positionX;PositionZ=positionZ;RoundGeneration=roundGeneration;}
        public long Sequence {get;}
        public long PlayerId {get;}
        public float PositionX {get;}
        public float PositionZ {get;}
        public int RoundGeneration {get;}
    }
}

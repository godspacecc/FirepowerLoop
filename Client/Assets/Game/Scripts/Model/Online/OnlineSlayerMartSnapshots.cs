using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;

namespace Game
{
    /// <summary>服务端生成的真实支援接触体；客户端只按身份和坐标投影。</summary>
    public readonly struct OnlineSlayerMartSupportSnapshot
    {
        public string ProductId { get; }
        public string InstanceId { get; }
        public SlayerMartSupportKind Kind { get; }
        public int BodyIndex { get; }
        public float PositionX { get; }
        public float PositionZ { get; }
        public float ShotDirectionX { get; }
        public float ShotDirectionZ { get; }
        public float RemainingSeconds { get; }
        public int Charges { get; }
        public OnlineSlayerMartSupportSnapshot(string productId,string instanceId,int kind,int bodyIndex,float x,float z,float shotX,float shotZ,float seconds,int charges)
        {ProductId=productId;InstanceId=instanceId;Kind=(SlayerMartSupportKind)kind;BodyIndex=bodyIndex;PositionX=x;PositionZ=z;ShotDirectionX=shotX;ShotDirectionZ=shotZ;RemainingSeconds=seconds;Charges=charges;}
    }
    /// <summary>公共区域不含现金、券或报价；Prepared指明服务器已建立区域地图代次。</summary>
    public sealed class OnlineSlayerMartRegionSnapshot
    {
        public int RulesVersion { get; }
        public bool Active { get; }
        public int Visit { get; }
        public int Generation { get; }
        public long Revision { get; }
        public bool Prepared { get; }
        public bool Ready { get; }
        public bool Closing { get; }
        public float RemainingSeconds { get; }
        public IReadOnlyList<ArcadeSlayerMartMemberSnapshot> Members { get; }
        public IReadOnlyList<int> CashBoxIds { get; }
        public OnlineSlayerMartRegionSnapshot(int version,bool active,int visit,int generation,long revision,bool prepared,bool ready,bool closing,float seconds,
            IEnumerable<ArcadeSlayerMartMemberSnapshot> members,IEnumerable<int> boxes)
        {RulesVersion=version;Active=active;Visit=visit;Generation=generation;Revision=revision;Prepared=prepared;Ready=ready;Closing=closing;RemainingSeconds=seconds;
            Members=Array.AsReadOnly(members.ToArray());CashBoxIds=Array.AsReadOnly(boxes.ToArray());}
    }
}

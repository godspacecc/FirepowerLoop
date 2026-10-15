using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>只保存有限坐标值，避免把 Unity 对象或 Vector3 派生属性写入 JSON。</summary>
    public sealed class ValleyPosition
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Yaw { get; set; }
        public Vector3 ToVector() => new Vector3(X, Y, Z);
        public static ValleyPosition From(Vector3 value) => new ValleyPosition { X = value.x, Y = value.y, Z = value.z };
    }
    public sealed class ValleyPointData
    {
        public string Id { get; set; }
        public string TemplateId { get; set; }
        public string RegionId { get; set; }
        public ValleyPosition Pose { get; set; }
    }
    public sealed class ValleyClueData
    {
        public string Id { get; set; }
        public int Chain { get; set; }
        public int Order { get; set; }
        public string MessageKey { get; set; }
        public ValleyPosition Pose { get; set; }
        public ValleyPosition Target { get; set; }
    }
    public sealed class ValleyEdgeData
    {
        public string Id { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public bool SafeRiverRoute { get; set; }
    }
    /// <summary>完整的当局地形与交互身份清单；保存实际采样而非要求读档重新运行生成算法。</summary>
    public sealed class ValleyLayoutSaveData
    {
        public int GenerationVersion { get; set; }
        public int Seed { get; set; }
        public int AttemptIndex { get; set; }
        public string MapId { get; set; }
        public int WorldSizeMeters { get; set; }
        public int HeightSampleSpacingMeters { get; set; }
        /// <summary>串联建议路线节点的长度，单位米；开放版允许横切，不代表玩家必须走的最短路。</summary>
        public float NominalRouteMeters { get; set; }
        /// <summary>按 z 行、x 列排列的完整高度网格，单位米；版本1/2均为257×257。</summary>
        public float[] Heights { get; set; }
        public List<ValleyPosition> Route { get; set; } = new List<ValleyPosition>();
        public List<ValleyPosition> AlternateRoute { get; set; } = new List<ValleyPosition>();
        public List<ValleyPosition> River { get; set; } = new List<ValleyPosition>();
        public List<ValleyPosition> Fords { get; set; } = new List<ValleyPosition>();
        public List<ValleyPosition> Shortcut { get; set; } = new List<ValleyPosition>();
        public List<ValleyEdgeData> Edges { get; set; } = new List<ValleyEdgeData>();
        public List<ValleyPointData> Points { get; set; } = new List<ValleyPointData>();
        public List<ValleyClueData> Clues { get; set; } = new List<ValleyClueData>();
        public ValleyPosition Spawn { get; set; }
        public ValleyPosition Camp { get; set; }
        /// <summary>版本3唯一实体撤离点；旧局为空，不补造点位。</summary>
        public ValleyPosition Extraction { get; set; }
        /// <summary>版本3可直接通行的支路与浅滩连接，保存实际折线。</summary>
        public List<System.Collections.Generic.List<ValleyPosition>> BranchRoads { get; set; } = new List<List<ValleyPosition>>();
        /// <summary>版本3敌人初始清单，TemplateId为敌人配置ID；运行变化另存世界快照。</summary>
        public List<ValleyPointData> Enemies { get; set; } = new List<ValleyPointData>();
        /// <summary>初始敌人清单已从章节配置收敛；空清单也必须保留，恢复时禁止补刷。</summary>
        public bool EnemySpawnsPrepared { get; set; }
        /// <summary>新候选从章节范围内抽取的敌人数；实际配置身份保存在Enemies。</summary>
        public int EnemyCount { get; set; }
        public string ShortcutId { get; set; }
        /// <summary>独立克隆清单及所有数组，失败回滚不共享当前局的可变引用。</summary>
        public ValleyLayoutSaveData Clone()
        {
            ValleyPosition Copy(ValleyPosition p)=>p==null?null:new ValleyPosition{X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw};
            List<ValleyPosition> CopyPath(List<ValleyPosition> path)=>path?.Select(Copy).ToList();
            // 地形数组在每次保存快照时直接复制，避免 JSON 中转产生整张地形的重复文本分配。
            return new ValleyLayoutSaveData
            {
                GenerationVersion=GenerationVersion,Seed=Seed,AttemptIndex=AttemptIndex,MapId=MapId,WorldSizeMeters=WorldSizeMeters,
                HeightSampleSpacingMeters=HeightSampleSpacingMeters,NominalRouteMeters=NominalRouteMeters,Heights=(float[])Heights?.Clone(),
                Route=CopyPath(Route),AlternateRoute=CopyPath(AlternateRoute),River=CopyPath(River),Fords=CopyPath(Fords),Shortcut=CopyPath(Shortcut),
                Edges=Edges?.Select(e=>e==null?null:new ValleyEdgeData{Id=e.Id,From=e.From,To=e.To,SafeRiverRoute=e.SafeRiverRoute}).ToList(),
                Points=Points?.Select(p=>p==null?null:new ValleyPointData{Id=p.Id,TemplateId=p.TemplateId,RegionId=p.RegionId,Pose=Copy(p.Pose)}).ToList(),
                Clues=Clues?.Select(c=>c==null?null:new ValleyClueData{Id=c.Id,Chain=c.Chain,Order=c.Order,MessageKey=c.MessageKey,Pose=Copy(c.Pose),Target=Copy(c.Target)}).ToList(),
                Spawn=Copy(Spawn),Camp=Copy(Camp),ShortcutId=ShortcutId,Extraction=Copy(Extraction),
                BranchRoads=BranchRoads?.Select(CopyPath).ToList(),EnemySpawnsPrepared=EnemySpawnsPrepared,EnemyCount=EnemyCount,
                Enemies=Enemies?.Select(p=>new ValleyPointData{Id=p.Id,TemplateId=p.TemplateId,RegionId=p.RegionId,Pose=Copy(p.Pose)}).ToList()
            };
        }
        public float HeightAt(float x, float z)
        {
            var n = WorldSizeMeters / HeightSampleSpacingMeters + 1;
            var fx = Mathf.Clamp((x + WorldSizeMeters * .5f) / HeightSampleSpacingMeters, 0, n - 1);
            var fz = Mathf.Clamp((z + WorldSizeMeters * .5f) / HeightSampleSpacingMeters, 0, n - 1);
            var ix = Mathf.Min(Mathf.FloorToInt(fx), n - 2); var iz = Mathf.Min(Mathf.FloorToInt(fz), n - 2);
            return Mathf.Lerp(Mathf.Lerp(Heights[iz*n+ix], Heights[iz*n+ix+1], fx-ix),
                Mathf.Lerp(Heights[(iz+1)*n+ix], Heights[(iz+1)*n+ix+1], fx-ix), fz-iz);
        }
    }
}

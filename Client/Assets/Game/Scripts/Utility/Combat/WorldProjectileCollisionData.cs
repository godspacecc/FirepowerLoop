using System;
namespace Game
{
    /// <summary>双端共用的地图本地坐标，单位米；不依赖 Unity 对象。</summary>
    [Serializable] public struct WorldCollisionVector
    {
        public float X,Y,Z;
        public WorldCollisionVector(float x,float y,float z){X=x;Y=y;Z=z;}
        public static WorldCollisionVector operator +(WorldCollisionVector a,WorldCollisionVector b)=>new WorldCollisionVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static WorldCollisionVector operator -(WorldCollisionVector a,WorldCollisionVector b)=>new WorldCollisionVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static WorldCollisionVector operator -(WorldCollisionVector a)=>new WorldCollisionVector(-a.X,-a.Y,-a.Z);
        public static WorldCollisionVector operator *(WorldCollisionVector a,float b)=>new WorldCollisionVector(a.X*b,a.Y*b,a.Z*b);
        public static float Dot(WorldCollisionVector a,WorldCollisionVector b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static WorldCollisionVector Cross(WorldCollisionVector a,WorldCollisionVector b)=>new WorldCollisionVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public float Length=>(float)Math.Sqrt(Dot(this,this));
        public WorldCollisionVector Normalized=>Length>1e-8f?this*(1f/Length):default;
        public bool IsFinite=>Finite(X)&&Finite(Y)&&Finite(Z);
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    }
    public enum WorldCollisionShapeKind{Box=1,Sphere=2,Capsule=3,Triangle=4,Heightfield=5}
    /// <summary>Box 使用中心和三个半轴；Capsule 使用端点和半径；Triangle 使用三个顶点。</summary>
    #if DOTNET
    [ET.EnableClass]
    #endif
    [Serializable] public sealed class WorldProjectileShape
    {
        public long Id;
        /// <summary>导出源的 GlobalObjectId 与形状序号，供错误定位；运行时排序仍使用 Id。</summary>
        public string SourceIdentity;
        public WorldCollisionShapeKind Kind;
        public WorldCollisionVector Center,AxisX,AxisY,AxisZ,A,B,C;
        public float Radius;
        /// <summary>Heightfield 的完整轴向尺寸存 AxisX/Y/Z，A 为原点；高度为行优先的归一化样本，孔洞是格子索引。</summary>
        public int HeightmapResolution;
        public float[] Heights;
        public int[] HoleCells;
        public WorldProjectileShape At(WorldCollisionVector center)=>new WorldProjectileShape{Id=Id,SourceIdentity=SourceIdentity,Kind=Kind,Center=center,AxisX=AxisX,AxisY=AxisY,AxisZ=AxisZ,A=A+(center-Center),B=B+(center-Center),C=C+(center-Center),Radius=Radius,HeightmapResolution=HeightmapResolution,Heights=Heights,HoleCells=HoleCells};
        public WorldCollisionVector Support(WorldCollisionVector d)
        {
            if(Kind==WorldCollisionShapeKind.Heightfield)return A+AxisX*(WorldCollisionVector.Dot(AxisX,d)>=0?1:0)+AxisY*(WorldCollisionVector.Dot(AxisY,d)>=0?1:0)+AxisZ*(WorldCollisionVector.Dot(AxisZ,d)>=0?1:0);
            if(Kind==WorldCollisionShapeKind.Box)return Center+AxisX*(WorldCollisionVector.Dot(AxisX,d)>=0?1:-1)+AxisY*(WorldCollisionVector.Dot(AxisY,d)>=0?1:-1)+AxisZ*(WorldCollisionVector.Dot(AxisZ,d)>=0?1:-1);
            if(Kind==WorldCollisionShapeKind.Sphere)return Center+d.Normalized*Radius;
            if(Kind==WorldCollisionShapeKind.Capsule)return(WorldCollisionVector.Dot(A,d)>=WorldCollisionVector.Dot(B,d)?A:B)+d.Normalized*Radius;
            var r=A;var best=WorldCollisionVector.Dot(A,d);if(WorldCollisionVector.Dot(B,d)>best){r=B;best=WorldCollisionVector.Dot(B,d);}return WorldCollisionVector.Dot(C,d)>best?C:r;
        }
        public void Bounds(out WorldCollisionVector min,out WorldCollisionVector max)
        {
            min=new WorldCollisionVector(Support(new WorldCollisionVector(-1,0,0)).X,Support(new WorldCollisionVector(0,-1,0)).Y,Support(new WorldCollisionVector(0,0,-1)).Z);
            max=new WorldCollisionVector(Support(new WorldCollisionVector(1,0,0)).X,Support(new WorldCollisionVector(0,1,0)).Y,Support(new WorldCollisionVector(0,0,1)).Z);
        }
    }
    /// <summary>离线导出的权威碰撞合同，依赖哈希用于阻止地图版本不一致的射击。</summary>
    #if DOTNET
    [ET.EnableClass]
    #endif
    [Serializable] public sealed class WorldProjectileCollisionData
    {
        public const int CurrentVersion=1;
        public int Version=CurrentVersion;
        public string MapId,ContentHash;
        public WorldProjectileShape[] Shapes=Array.Empty<WorldProjectileShape>();
        public WorldProjectileCollisionProfile[] Profiles=Array.Empty<WorldProjectileCollisionProfile>();
        /// <summary>角色包围体界定的近身发射点距离上限，单位米。</summary>
        public float MaxOriginDistance;
    }
    #if DOTNET
    [ET.EnableClass]
    #endif
    [Serializable] public sealed class WorldProjectileCollisionProfile
    { public string ConfigId;public WorldProjectileShape[] Shapes=Array.Empty<WorldProjectileShape>(); }
    public readonly struct WorldProjectileContact
    {
        public readonly long ShapeId;public readonly float Distance;public readonly WorldCollisionVector Position;
        public WorldProjectileContact(long id,float distance,WorldCollisionVector position){ShapeId=id;Distance=distance;Position=position;}
    }
}

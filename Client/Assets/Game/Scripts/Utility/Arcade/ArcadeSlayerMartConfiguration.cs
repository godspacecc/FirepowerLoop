using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
#if DOTNET
using SlayerTables = ET.Tables;
using SlayerPowerupBehavior = ET.ArcadePowerupBehavior;
using SlayerDropKind = ET.ArcadeDropKind;
#else
using SlayerTables = global::Tables;
using SlayerPowerupBehavior = global::ArcadePowerupBehavior;
using SlayerDropKind = global::ArcadeDropKind;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>商店现金箱的稳定配置坐标，不包含运行领取状态。</summary>
    public readonly struct ArcadeSlayerMartPoint
    {
        public readonly float X,Y;
        public ArcadeSlayerMartPoint(float x,float y){X=x;Y=y;}
    }
    /// <summary>区域只读几何，单人与服务端以同一坐标投影和检测领取。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartRegionLayout
    {
        public int Id { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }
        public float SpawnX { get; }
        public float SpawnY { get; }
        public float ExitX { get; }
        public float ExitY { get; }
        public float PickupRadius { get; }
        public IReadOnlyList<ArcadeSlayerMartPoint> Boxes { get; }
        public ArcadeSlayerMartRegionLayout(int id,float halfWidth,float halfHeight,float spawnX,float spawnY,float exitX,float exitY,float radius,IEnumerable<ArcadeSlayerMartPoint> boxes)
        {
            Id=id;HalfWidth=halfWidth;HalfHeight=halfHeight;SpawnX=spawnX;SpawnY=spawnY;ExitX=exitX;ExitY=exitY;PickupRadius=radius;
            Boxes=Array.AsReadOnly(boxes.ToArray());
            foreach(float value in new[]{halfWidth,halfHeight,spawnX,spawnY,exitX,exitY,radius})
                if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("商店区域坐标无效");
            if(halfWidth<=0||halfHeight<=0||radius<=0||Math.Abs(spawnX)+radius>=halfWidth||Math.Abs(spawnY)+radius>=halfHeight||
                Math.Abs(exitX)+radius>=halfWidth||Math.Abs(exitY)+radius>=halfHeight||Boxes.Count==0||
                Boxes.Any(p=>float.IsNaN(p.X)||float.IsNaN(p.Y)||float.IsInfinity(p.X)||float.IsInfinity(p.Y)||Math.Abs(p.X)+radius>=halfWidth||Math.Abs(p.Y)+radius>=halfHeight)||
                Boxes.Select(p=>(p.X,p.Y)).Distinct().Count()!=Boxes.Count)
                throw new ArgumentException("商店现金箱重复、不可达或超出边界");
        }
    }
    /// <summary>正式双端配置的不可变适配；不自行补缺失表或数值。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartConfiguration
    {
        /// <summary>网络能力标签与存档配置指纹分离，拒绝旧协议而不改写旧局保存身份。</summary>
        public static string NetworkFingerprint(string configFingerprint)=>string.IsNullOrWhiteSpace(configFingerprint)?string.Empty:configFingerprint+"|slayermart.v2|coop-rescue.v2|score-shop.v1";
        public ArcadeSlayerMartCatalog Catalog { get; private set; }
        public bool Enabled { get; private set; }
        public IReadOnlyList<ArcadeSlayerMartRegionLayout> Regions { get; private set; }
        public ArcadeSlayerMartSupportCatalog Supports { get; private set; }
        private IReadOnlyDictionary<string,float> parameters;
        /// <summary>缺值直接拒绝，不用硬编码默认值继续运行。</summary>
        public float Parameter(string id) => parameters.TryGetValue(id,out float value)?value:throw new ArgumentException("缺少商店参数："+id);
    }
}

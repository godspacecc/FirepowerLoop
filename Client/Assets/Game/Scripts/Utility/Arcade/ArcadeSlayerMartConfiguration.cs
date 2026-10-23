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
        public static ArcadeSlayerMartConfiguration FromTables(SlayerTables tables)
        {
            if(tables==null)throw new ArgumentNullException(nameof(tables));
            var c=new ArcadeSlayerMartConfiguration();
            c.parameters=new ReadOnlyDictionary<string,float>(tables.TbArcadeSlayerMartAbility.DataMap.ToDictionary(p=>p.Key,p=>p.Value.Value,StringComparer.Ordinal));
            foreach(string key in new[]{"release.enabled","inventory.cards","inventory.maxLives","inventory.maxBombs","inventory.maxDashes","inventory.maxChickens",
                "storm.kills","storm.duration","storm.radius","storm.interval","storm.damage","storm.eliteSpeedScale","mastery.magnet","mastery.decoy","mastery.gloves",
                "mastery.chicken","mastery.barrel","mastery.sawDistance","mastery.deadOperator","mastery.sentry","mastery.respawn","support.orbitSpeed","support.orbitRadius",
                "support.contactRadius","support.barrelCharges","support.barrelInterval","support.orbKnockback","gem.minimum","gem.maximum"})
            {
                float value=c.Parameter(key);
                if(float.IsNaN(value)||float.IsInfinity(value)||value<0||(key!="release.enabled"&&value==0))throw new ArgumentException("商店参数无效："+key);
            }
            if(c.Parameter("release.enabled")!=0&&c.Parameter("release.enabled")!=1)throw new ArgumentException("商店开放门禁必须为0或1");
            foreach(string key in new[]{"inventory.cards","inventory.maxLives","inventory.maxBombs","inventory.maxDashes","inventory.maxChickens","storm.kills","storm.damage","support.barrelCharges","gem.minimum","gem.maximum"})
                if((double)c.Parameter(key)>int.MaxValue||Math.Floor(c.Parameter(key))!=c.Parameter(key))throw new ArgumentException("商店整数参数无效："+key);
            if(c.Parameter("storm.eliteSpeedScale")>1||c.Parameter("gem.minimum")>c.Parameter("gem.maximum"))throw new ArgumentException("商店比例或随机范围无效");
            c.Enabled=c.Parameter("release.enabled")==1;
            var items=tables.TbArcadeSlayerMartItem.DataList.OrderBy(p=>p.Effect).Select(p=>
            {
                if(!Enum.IsDefined(typeof(SlayerMartEffect),p.Effect)||p.Id!="slayermart.item."+p.Effect.ToString("00",System.Globalization.CultureInfo.InvariantCulture)||
                    string.IsNullOrWhiteSpace(p.NameKey)||string.IsNullOrWhiteSpace(p.DescKey))throw new ArgumentException("商店商品身份或本地化键无效："+p.Id);
                return new ArcadeSlayerMartItem(p.Id,(SlayerMartEffect)p.Effect,p.Price,p.OncePerRun,p.Amount,p.Duration);
            }).ToArray();
            var regions=tables.TbArcadeSlayerMartRegion.DataList.OrderBy(p=>p.Id).ToArray();
            c.Regions=Array.AsReadOnly(regions.Select(p=>new ArcadeSlayerMartRegionLayout(p.Id,p.HalfWidth,p.HalfHeight,p.SpawnX,p.SpawnY,p.ExitX,p.ExitY,p.PickupRadius,
                p.CashBoxes.Select(b=>new ArcadeSlayerMartPoint(b.X,b.Y)))).ToArray());
            c.Catalog=new ArcadeSlayerMartCatalog(items,regions.Select(p=>new ArcadeSlayerMartRegion(p.Id,p.AfterRound,p.CashBoxes.Length,p.CashPerMember,p.Duration)),
                (int)c.Parameter("inventory.cards"),(int)c.Parameter("inventory.maxLives"),(int)c.Parameter("inventory.maxBombs"),(int)c.Parameter("inventory.maxDashes"),(int)c.Parameter("inventory.maxChickens"),
                (int)c.Parameter("gem.minimum"),(int)c.Parameter("gem.maximum"));
            c.Supports=new ArcadeSlayerMartSupportCatalog(Enum.GetValues(typeof(SlayerMartSupportKind)).Cast<SlayerMartSupportKind>().Select(kind=>
            {
                var behavior=kind==SlayerMartSupportKind.Orb?SlayerPowerupBehavior.ElectricOrb:kind==SlayerMartSupportKind.Chicken?SlayerPowerupBehavior.Chicken:
                    kind==SlayerMartSupportKind.Saw?SlayerPowerupBehavior.Saw:SlayerPowerupBehavior.Barrel;
                var source=tables.TbArcadePowerup.DataList.Single(p=>p.Behavior==behavior);
                var drop=tables.TbArcadeDrop.DataList.Single(p=>p.Kind==SlayerDropKind.Powerup&&p.ProductId==source.Id);
                return new ArcadeSlayerMartSupportDefinition(kind,source.Id,drop.DurationSeconds,source.Damage,
                    kind==SlayerMartSupportKind.Barrel?c.Parameter("support.barrelInterval"):source.IntervalSeconds,source.Radius,source.ProjectileSpeed,
                    kind==SlayerMartSupportKind.Barrel?(int)c.Parameter("support.barrelCharges"):source.Charges);
            }));
            return c;
        }
    }
}

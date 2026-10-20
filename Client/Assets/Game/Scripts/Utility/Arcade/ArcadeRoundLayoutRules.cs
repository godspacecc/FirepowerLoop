using System;
using System.Collections.Generic;
using System.Linq;


#if DOTNET
using LayoutConfig = ET.ArcadeRoundLayoutConfig;
#else
using LayoutConfig = global::ArcadeRoundLayoutConfig;
#endif
namespace FirepowerLoop.Arcade
{
    /// <summary>双端平面阻挡；和主题地标使用相同的中心与半尺寸。</summary>
    public readonly struct ArcadeLayoutObstacle
    {
        public ArcadeLayoutObstacle(float x,float y,float halfWidth,float halfHeight)
        { X=x;Y=y;HalfWidth=halfWidth;HalfHeight=halfHeight; }
        public float X { get; }
        public float Y { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }
    }
    /// <summary>只读轮级地图及特殊内容配置；没有 Unity 依赖，不持有战斗状态。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeRoundLayoutParameters
    {
        /// <summary>普通场地按北、东、南、西各提供一个同去向出口。</summary>
        public const int ExitDirectionCount = 4;
        private ArcadeRoundLayoutParameters(LayoutConfig row,IEnumerable<ArcadeRegionPoint> barrels=null)
        {
            Id=row.Id;ThemeKey=row.ThemeKey;NameKey=row.NameKey;ArenaConfigNumber=row.ArenaConfigNumber;
            RoundNumber=row.RoundNumber;Version=row.Version;SpawnX=row.SpawnX;SpawnY=row.SpawnY;
            ExitX=row.ExitX;ExitY=row.ExitY;ExitSpacing=row.ExitSpacing;
            Obstacles=Array.AsReadOnly(row.Obstacles.Select(o=>new ArcadeLayoutObstacle(o.CenterX,o.CenterY,o.HalfWidth,o.HalfHeight)).ToArray());
            Entries=Array.AsReadOnly(row.Entries.Select(e=>new ArcadeRegionPoint(e.X,e.Y)).ToArray());
            Barrels=Array.AsReadOnly((barrels??row.Barrels.Select(e=>new ArcadeRegionPoint(e.X,e.Y))).ToArray());
            BarrelDropSeconds=row.BarrelDropSeconds;BarrelPullRadius=row.BarrelPullRadius;
            BarrelPullSpeed=row.BarrelPullSpeed;BarrelHealth=row.BarrelHealth;
            WeaponSpawnPoints=Array.AsReadOnly(row.WeaponSpawnPoints.Select(e=>new ArcadeRegionPoint(e.X,e.Y)).ToArray());
            WeaponSpawnCount=row.WeaponSpawnCount;
            WeaponSpawnDelayMin=row.WeaponSpawnDelayMin;WeaponSpawnDelayMax=row.WeaponSpawnDelayMax;
            WeaponSpawnFirstDelayMin=row.WeaponSpawnFirstDelayMin;WeaponSpawnFirstDelayMax=row.WeaponSpawnFirstDelayMax;
            SpecialKind=row.SpecialKind;EventKind=row.EventKind;BossKind=row.BossKind;SpecialRatio=row.SpecialRatio;
            EnemyHealthScale=row.EnemyHealthScale;EnemySpeedScale=row.EnemySpeedScale;SkillPeriod=row.SkillPeriod;
            SkillWarning=row.SkillWarning;SkillMagnitude=row.SkillMagnitude;EventPeriod=row.EventPeriod;
            EventWarning=row.EventWarning;EventRadius=row.EventRadius;EventDamage=row.EventDamage;EventMoveSpeed=row.EventMoveSpeed;
        }
        public string Id { get; }
        public string ThemeKey { get; }
        public string NameKey { get; }
        public int ArenaConfigNumber { get; }
        public int RoundNumber { get; }
        public int Version { get; }
        public float SpawnX { get; }
        public float SpawnY { get; }
        public float ExitX { get; }
        public float ExitY { get; }
        public float ExitSpacing { get; }
        /// <summary>规则与表现共用四向落点；海岛沿用干陆地内侧的留量。</summary>
        public ArcadeRegionPoint GetExitPosition(int direction, float halfWidth, float halfHeight, float playerRadius)
            => DirectionalExit(direction, ArenaConfigNumber == 1 ? 0 : ExitX,
                ArenaConfigNumber == 1 ? halfHeight - 3 : ExitY,
                halfWidth, halfHeight, playerRadius, ArenaConfigNumber == 1 ? RoundNumber : 0);

        /// <summary>把北侧出口依次旋转到四边；海岛中心落在干陆地，外缘保留完整触发半径。</summary>
        public static ArcadeRegionPoint DirectionalExit(int direction, float northX, float northY,
            float halfWidth, float halfHeight, float playerRadius, int islandRound = 0)
        {
            if (direction < 0 || direction >= ExitDirectionCount) throw new ArgumentOutOfRangeException(nameof(direction));
            var point = direction switch
            {
                0 => new ArcadeRegionPoint(northX, northY),
                1 => new ArcadeRegionPoint(northY * halfWidth / halfHeight, -northX * halfHeight / halfWidth),
                2 => new ArcadeRegionPoint(-northX, -northY),
                _ => new ArcadeRegionPoint(-northY * halfWidth / halfHeight, northX * halfHeight / halfWidth)
            };
            if (islandRound == 0) return point;
            var region = new ArcadeIslandRegion(halfWidth, halfHeight, islandRound);
            point = region.LandPoint(point.X, point.Y, playerRadius);
            return region.Constrain(point.X, point.Y, playerRadius + 1.5f);
        }
        public IReadOnlyList<ArcadeLayoutObstacle> Obstacles { get; }
        public IReadOnlyList<ArcadeRegionPoint> Entries { get; }
        /// <summary>预置可射桶坐标；数组索引是当前地图版本内的稳定身份。</summary>
        public IReadOnlyList<ArcadeRegionPoint> Barrels { get; }
        /// <summary>载体从出生到放桶的活动秒数；落地桶吸取半径、速度和独立耐久。</summary>
        public float BarrelDropSeconds { get; }
        public float BarrelPullRadius { get; }
        public float BarrelPullSpeed { get; }
        public int BarrelHealth { get; }
        /// <summary>本图固定武器候选点；只在轮开始时抽取，场景表现不决定生成位置。</summary>
        public IReadOnlyList<ArcadeRegionPoint> WeaponSpawnPoints { get; }
        /// <summary>每轮分批投放数量，仍受地面物资总预算限制。</summary>
        public int WeaponSpawnCount { get; }
        /// <summary>首把投放之后，下一把出现前的活动战斗等待秒数范围。</summary>
        public float WeaponSpawnDelayMin { get; }
        public float WeaponSpawnDelayMax { get; }
        /// <summary>开战到首把空投的独立短等待范围，不随后续间隔延长。</summary>
        public float WeaponSpawnFirstDelayMin { get; }
        public float WeaponSpawnFirstDelayMax { get; }

        /// <summary>轮开始时只准备点位和计时，不立即创建可见掉落。</summary>
        public ArcadeWeaponSpawnSchedule CreateWeaponSpawnSchedule(Random random) => new ArcadeWeaponSpawnSchedule(
            ChooseWeaponSpawnPoints(WeaponSpawnCount,random.Next),WeaponSpawnFirstDelayMin,WeaponSpawnFirstDelayMax,
            WeaponSpawnDelayMin,WeaponSpawnDelayMax,random);

        /// <summary>使用调用方的权威随机源，无放回抽取候选点；不修改配置或持有对局状态。</summary>
        public IReadOnlyList<ArcadeRegionPoint> ChooseWeaponSpawnPoints(int available,Func<int,int> next)
        {
            if(next==null)throw new ArgumentNullException(nameof(next));
            int count=Math.Min(WeaponSpawnCount,Math.Max(0,available));
            var points=WeaponSpawnPoints.ToArray();
            for(int index=0;index<count;index++)
            {
                int selected=index+next(points.Length-index);
                var point=points[index];points[index]=points[selected];points[selected]=point;
            }
            return Array.AsReadOnly(points.Take(count).ToArray());
        }
        public int SpecialKind { get; }
        public int EventKind { get; }
        public int BossKind { get; }
        public float SpecialRatio { get; }
        public float EnemyHealthScale { get; }
        public float EnemySpeedScale { get; }
        public float SkillPeriod { get; }
        public float SkillWarning { get; }
        public float SkillMagnitude { get; }
        public float EventPeriod { get; }
        public float EventWarning { get; }
        public float EventRadius { get; }
        public float EventDamage { get; }
        public float EventMoveSpeed { get; }
        /// <summary>从唯一配置源复制数值并拒绝无效身份、未知版本、枚举和非有限值。</summary>
        public static ArcadeRoundLayoutParameters FromConfig(LayoutConfig row)
        {
            if(row==null)throw new ArgumentNullException(nameof(row));
            var p=new ArcadeRoundLayoutParameters(row);
            var values=new[]{p.SpawnX,p.SpawnY,p.ExitX,p.ExitY,p.ExitSpacing,p.SpecialRatio,p.EnemyHealthScale,p.EnemySpeedScale,
                p.SkillPeriod,p.SkillWarning,p.SkillMagnitude,p.EventPeriod,p.EventWarning,p.EventRadius,p.EventDamage,p.EventMoveSpeed};
            if(values.Any(v=>!Finite(v)) || p.ArenaConfigNumber<1 || p.RoundNumber<1 || p.RoundNumber>4 || p.Version!=(p.ArenaConfigNumber==3?2:1) ||
                p.Id!="arena."+p.ArenaConfigNumber+".round."+p.RoundNumber || string.IsNullOrWhiteSpace(p.ThemeKey) ||
                string.IsNullOrWhiteSpace(p.NameKey) || p.ExitSpacing<=0 || p.Entries.Count==0 ||
                p.SpecialKind<0 || p.SpecialKind>17 || p.EventKind<0 || p.EventKind>10 || p.BossKind<0 || p.BossKind>4 ||
                p.SpecialRatio<0 || p.SpecialRatio>1 || p.EnemyHealthScale<=0 || p.EnemySpeedScale<=0 ||
                p.SkillPeriod<=0 || p.SkillWarning<=0 || p.SkillWarning>=p.SkillPeriod || p.SkillMagnitude<=0 ||
                p.EventPeriod<=0 || p.EventWarning<=0 || p.EventWarning>=p.EventPeriod || p.EventRadius<=0 || p.EventDamage<0 || p.EventDamage>1 || p.EventMoveSpeed<0 ||
                p.Obstacles.Any(o=>!Finite(o.X)||!Finite(o.Y)||!Finite(o.HalfWidth)||!Finite(o.HalfHeight)||o.HalfWidth<=0||o.HalfHeight<=0) ||
                p.Entries.Any(e=>!Finite(e.X)||!Finite(e.Y)) ||
                p.WeaponSpawnCount<0 || p.WeaponSpawnCount>p.WeaponSpawnPoints.Count ||
                !Finite(p.WeaponSpawnDelayMin)||!Finite(p.WeaponSpawnDelayMax)||p.WeaponSpawnDelayMin<=0||p.WeaponSpawnDelayMax<p.WeaponSpawnDelayMin ||
                !Finite(p.WeaponSpawnFirstDelayMin)||!Finite(p.WeaponSpawnFirstDelayMax)||p.WeaponSpawnFirstDelayMin<=0||p.WeaponSpawnFirstDelayMax<p.WeaponSpawnFirstDelayMin ||
                p.WeaponSpawnPoints.Any(e=>!Finite(e.X)||!Finite(e.Y)||p.OverlapsWeaponLandmark(e)) ||
                p.Barrels.Any(e=>!Finite(e.X)||!Finite(e.Y)||p.OverlapsBarrelLandmark(e)))throw new ArgumentException("轮级布局参数无效："+p.Id);
            if(!Finite(p.BarrelDropSeconds)||p.BarrelDropSeconds<=0||!Finite(p.BarrelPullRadius)||p.BarrelPullRadius<=0||
                !Finite(p.BarrelPullSpeed)||p.BarrelPullSpeed<=0||p.BarrelHealth<=0)
                throw new ArgumentException("漩涡桶参数无效："+p.Id);
            return p;
        }
        /// <summary>武器点位不能重叠或占用出生、入口、出口、预置桶和实体障碍。</summary>
        private bool OverlapsWeaponLandmark(ArcadeRegionPoint point)
        {
            bool Near(ArcadeRegionPoint other,float distance)=>
                (point.X-other.X)*(point.X-other.X)+(point.Y-other.Y)*(point.Y-other.Y)<distance*distance;
            return Near(new ArcadeRegionPoint(SpawnX,SpawnY),3) || Entries.Any(e=>Near(e,2)) ||
                Enumerable.Range(0,ExitDirectionCount).Any(i=>Near(DirectionalExit(i,ExitX,ExitY,1,1,0),2)) ||
                Barrels.Any(e=>Near(e,1.5f)) || WeaponSpawnPoints.Count(e=>Near(e,2))>1 ||
                Obstacles.Any(o=>Math.Abs(point.X-o.X)<o.HalfWidth+1.1f&&Math.Abs(point.Y-o.Y)<o.HalfHeight+1.1f);
        }
        /// <summary>桶保留半米占地，不能占用出生点、全部出口或地图阻挡。</summary>
        private bool OverlapsBarrelLandmark(ArcadeRegionPoint barrel)
        {
            const float radius=.5f;
            if(Obstacles.Any(o=>Math.Abs(barrel.X-o.X)<o.HalfWidth+radius&&Math.Abs(barrel.Y-o.Y)<o.HalfHeight+radius))return true;
            var points=Enumerable.Range(0,ExitDirectionCount).Select(i=>DirectionalExit(i,ExitX,ExitY,1,1,0))
                .Append(new ArcadeRegionPoint(SpawnX,SpawnY));
            return points.Any(p=>Math.Abs(barrel.X-p.X)<radius*2&&Math.Abs(barrel.Y-p.Y)<radius*2) ||
                Barrels.Count(p=>Math.Abs(barrel.X-p.X)<radius*2&&Math.Abs(barrel.Y-p.Y)<radius*2)>1;
        }
        /// <summary>矩形场地或保留的海岛岸线内，使用膨胀阻挡判断真实行走位置。</summary>
        public bool IsWalkable(float x,float y,float radius,float halfWidth=19.5f,float halfHeight=19.5f)
            => IsWalkable(x,y,radius,halfWidth,halfHeight,ArenaConfigNumber==1 ? new ArcadeIslandRegion(halfWidth,halfHeight,RoundNumber) : null);
        private bool IsWalkable(float x,float y,float radius,float halfWidth,float halfHeight,ArcadeIslandRegion region)
        {
            if(!Finite(x)||!Finite(y)||!Finite(radius)||radius<0 || Math.Abs(x)+radius>halfWidth || Math.Abs(y)+radius>halfHeight)return false;
            if(region!=null)
            {
                var constrained=region.Constrain(x,y,radius);
                if(Math.Abs(constrained.X-x)>.01f || Math.Abs(constrained.Y-y)>.01f)return false;
            }
            return !Obstacles.Any(o=>Math.Abs(x-o.X)<o.HalfWidth+radius && Math.Abs(y-o.Y)<o.HalfHeight+radius);
        }
        /// <summary>目录加载时按两人并行直径检查出生、四向入口与所有可能出口的连通区域。</summary>
        public bool FitsArena(float halfWidth,float halfHeight,float playerRadius)
        {
            if(!Finite(halfWidth)||!Finite(halfHeight)||!Finite(playerRadius)||halfWidth<=5||halfHeight<=5||playerRadius<=0)return false;
            float clearance=playerRadius*2;
            var region=ArenaConfigNumber==1 ? new ArcadeIslandRegion(halfWidth,halfHeight,RoundNumber) : null;
            var targets=new List<ArcadeRegionPoint>(Entries){new ArcadeRegionPoint(SpawnX,SpawnY)};
            targets.AddRange(Enumerable.Range(0,ExitDirectionCount).Select(i=>GetExitPosition(i,halfWidth,halfHeight,playerRadius)));
            targets.AddRange(Barrels);
            targets.AddRange(WeaponSpawnPoints);
            if(targets.Any(p=>!IsWalkable(p.X,p.Y,clearance,halfWidth,halfHeight,region)) ||
                Obstacles.Any(o=>Math.Abs(o.X)+o.HalfWidth>=halfWidth||Math.Abs(o.Y)+o.HalfHeight>=halfHeight))return false;
            // 半米网格比最窄的并行通路更细；只用于装载校验，不参与每帧移动。
            const float step=.5f;
            int columns=(int)(halfWidth*4)+1,rows=(int)(halfHeight*4)+1;
            int Index(float x,float y)=>(int)Math.Round((y+halfHeight)/step)*columns+(int)Math.Round((x+halfWidth)/step);
            var seen=new HashSet<int>();var pending=new Queue<int>();int start=Index(SpawnX,SpawnY);seen.Add(start);pending.Enqueue(start);
            var directions=new[]{new ArcadeRegionPoint(-1,0),new ArcadeRegionPoint(1,0),new ArcadeRegionPoint(0,-1),new ArcadeRegionPoint(0,1)};
            while(pending.Count>0)
            {
                int current=pending.Dequeue(),cx=current%columns,cy=current/columns;
                foreach(var delta in directions)
                {
                    int x=cx+(int)delta.X,y=cy+(int)delta.Y,index=y*columns+x;
                    if(x<0||y<0||x>=columns||y>=rows||seen.Contains(index)||!IsWalkable(x*step-halfWidth,y*step-halfHeight,clearance,halfWidth,halfHeight,region))continue;
                    seen.Add(index);pending.Enqueue(index);
                }
            }
            return targets.All(p=>seen.Contains(Index(p.X,p.Y)));
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }

    /// <summary>双端共用的轮内武器刷新时钟；每次最多交付一个点位，卡顿不追赶连发。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeWeaponSpawnSchedule
    {
        /// <summary>本轮已抽取且不重复的候选点。</summary>
        private readonly IReadOnlyList<ArcadeRegionPoint> points;
        /// <summary>由对局权威提供的随机源，统一生成间隔和武器权重。</summary>
        private readonly Random random;
        /// <summary>首把之后的投放间隔范围，避免两把都集中在开场。</summary>
        private readonly float delayMin,delayMax;
        /// <summary>下一个尚未交付的点位索引。</summary>
        private int next;
        public int RemainingCount=>points.Count-next;
        /// <summary>只由活动战斗步长推进；暂停和场景切换不调用推进。</summary>
        public float RemainingSeconds { get; private set; }

        /// <summary>首次与后续间隔分别取配置范围，随机源和计时仍由原战斗权威持有。</summary>
        internal ArcadeWeaponSpawnSchedule(IReadOnlyList<ArcadeRegionPoint> points,float firstDelayMin,float firstDelayMax,
            float delayMin,float delayMax,Random random)
        {
            this.points=points;this.delayMin=delayMin;this.delayMax=delayMax;this.random=random;
            RemainingSeconds=NextDelay(firstDelayMin,firstDelayMax);
        }
        /// <summary>在指定活动秒数范围内取一次随机等待，不追赶已错过的投放。</summary>
        private float NextDelay(float minimum,float maximum)=>minimum+(float)random.NextDouble()*(maximum-minimum);
        /// <summary>沿用当前进度武器池的权重随机，不让表现端重新选择。</summary>
        public int NextWeaponRoll(int totalWeight)=>random.Next(totalWeight);

        /// <summary>预算不足保留待刷点位；恢复空间后只补一把，并为下一把重新计时。</summary>
        public bool TryAdvance(float seconds,bool hasCapacity,out ArcadeRegionPoint point)
        {
            point=default;
            if(RemainingCount<=0||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return false;
            RemainingSeconds=Math.Max(0,RemainingSeconds-seconds);
            if(RemainingSeconds>0||!hasCapacity)return false;
            point=points[next++];
            RemainingSeconds=RemainingCount>0?NextDelay(delayMin,delayMax):0;
            return true;
        }
    }
}

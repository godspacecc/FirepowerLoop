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
        private ArcadeRoundLayoutParameters(LayoutConfig row)
        {
            Id=row.Id;ThemeKey=row.ThemeKey;NameKey=row.NameKey;ArenaConfigNumber=row.ArenaConfigNumber;
            RoundNumber=row.RoundNumber;Version=row.Version;SpawnX=row.SpawnX;SpawnY=row.SpawnY;
            ExitX=row.ExitX;ExitY=row.ExitY;ExitSpacing=row.ExitSpacing;
            Obstacles=Array.AsReadOnly(row.Obstacles.Select(o=>new ArcadeLayoutObstacle(o.CenterX,o.CenterY,o.HalfWidth,o.HalfHeight)).ToArray());
            Entries=Array.AsReadOnly(row.Entries.Select(e=>new ArcadeRegionPoint(e.X,e.Y)).ToArray());
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
        public IReadOnlyList<ArcadeLayoutObstacle> Obstacles { get; }
        public IReadOnlyList<ArcadeRegionPoint> Entries { get; }
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
            if(values.Any(v=>!Finite(v)) || p.ArenaConfigNumber<1 || p.RoundNumber<1 || p.RoundNumber>4 || p.Version!=1 ||
                p.Id!="arena."+p.ArenaConfigNumber+".round."+p.RoundNumber || string.IsNullOrWhiteSpace(p.ThemeKey) ||
                string.IsNullOrWhiteSpace(p.NameKey) || p.ExitSpacing<=0 || p.Entries.Count==0 ||
                p.SpecialKind<0 || p.SpecialKind>17 || p.EventKind<0 || p.EventKind>10 || p.BossKind<0 || p.BossKind>4 ||
                p.SpecialRatio<0 || p.SpecialRatio>1 || p.EnemyHealthScale<=0 || p.EnemySpeedScale<=0 ||
                p.SkillPeriod<=0 || p.SkillWarning<=0 || p.SkillWarning>=p.SkillPeriod || p.SkillMagnitude<=0 ||
                p.EventPeriod<=0 || p.EventWarning<=0 || p.EventWarning>=p.EventPeriod || p.EventRadius<=0 || p.EventDamage<0 || p.EventDamage>1 || p.EventMoveSpeed<0 ||
                p.Obstacles.Any(o=>!Finite(o.X)||!Finite(o.Y)||!Finite(o.HalfWidth)||!Finite(o.HalfHeight)||o.HalfWidth<=0||o.HalfHeight<=0) ||
                p.Entries.Any(e=>!Finite(e.X)||!Finite(e.Y)))throw new ArgumentException("轮级布局参数无效："+p.Id);
            return p;
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
            var targets=new List<ArcadeRegionPoint>(Entries){new ArcadeRegionPoint(SpawnX,SpawnY),new ArcadeRegionPoint(ExitX,ExitY),
                new ArcadeRegionPoint(ExitX-ExitSpacing*1.5f,ExitY),new ArcadeRegionPoint(ExitX+ExitSpacing*1.5f,ExitY)};
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
}

using System;
using System.Collections.Generic;

namespace FirepowerLoop.Arcade
{
    /// <summary>Guardian瞬时攻击状态，只随当前独立场生存，不进入主线存档。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerGuardianState
    {
        /// <summary>仅活动模拟推进的累计秒数。</summary>
        public double Elapsed {get;set;}
        /// <summary>已锁定预警与已释放攻击的周期，禁止重复施放。</summary>
        public long Cycle=-1,AttackCycle=-1;
        /// <summary>当前预警锁定的起点与目标点，不在释放前追踪玩家。</summary>
        public float OriginX,OriginY,TargetX,TargetY;
        /// <summary>本独立场内区域与火球的稳定递增身份。</summary>
        public int NextId=1;
        /// <summary>尚未到期的持续伤害区域。</summary>
        public readonly List<ArcadeSlayerGuardianArea> Areas=new List<ArcadeSlayerGuardianArea>();
        /// <summary>仍在飞行的火球；终点或碰撞后即移除。</summary>
        public readonly List<ArcadeSlayerGuardianBall> Balls=new List<ArcadeSlayerGuardianBall>();
        /// <summary>本帧只读表现投影，不接受表现端伤害回写。</summary>
        public readonly List<ArcadeContentAreaSnapshot> Snapshots=new List<ArcadeContentAreaSnapshot>();
    }
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerGuardianArea
    {
        /// <summary>区域身份、两端及剩余活动时间；火道穿越石柱。</summary>
        public int Id,Kind;
        public float X,Y,EndX,EndY,Radius,Remaining,UntilTick;
        public bool Segment;
    }
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerGuardianBall
    {
        /// <summary>朝预警锁定点飞行，不追踪玩家；到达或碰撞即爆炸。</summary>
        public int Id;
        public float X,Y,TargetX,TargetY;
    }
    /// <summary>宿主提供的存活目标逻辑坐标，不持有角色对象。</summary>
    public readonly struct ArcadeSlayerGuardianTarget
    {
        /// <summary>原始成员身份与当前场内坐标。</summary>
        public readonly long Id;
        public readonly float X,Y;
        public ArcadeSlayerGuardianTarget(long id,float x,float y){Id=id;X=x;Y=y;}
    }
    /// <summary>同源攻击命中意图，实际扣血及保护仍由宿主裁决。</summary>
    public readonly struct ArcadeSlayerGuardianHit
    {
        /// <summary>受击成员、基础伤害及致命机关标记。</summary>
        public readonly long Id;
        public readonly int Damage;
        public readonly bool Lethal;
        public ArcadeSlayerGuardianHit(long id,int damage,bool lethal=false){Id=id;Damage=damage;Lethal=lethal;}
    }
    /// <summary>双端同源Guardian三技能裁决；宿主仍负责无敌、护盾、生命和独立倒地。</summary>
    public static class ArcadeSlayerGuardianCombat
    {
        /// <summary>点到有限线段的距离；圆区用相同起终点退化表示。</summary>
        public static float DistanceToSegment(float x,float y,float ax,float ay,float bx,float by)
        {
            float dx=bx-ax,dy=by-ay,d=dx*dx+dy*dy;
            float t=d<=0?0:Math.Max(0,Math.Min(1,((x-ax)*dx+(y-ay)*dy)/d));
            dx=x-ax-t*dx;dy=y-ay-t*dy;return (float)Math.Sqrt(dx*dx+dy*dy);
        }
        private static bool InArea(ArcadeSlayerGuardianTarget t,ArcadeSlayerGuardianArea a,float body)
            =>DistanceToSegment(t.X,t.Y,a.X,a.Y,a.Segment?a.EndX:a.X,a.Segment?a.EndY:a.Y)<=a.Radius+body;
        /// <summary>仅推进活动帧；不补发漏过周期的多次攻击，区域每帧最多一次伤害。</summary>
        public static List<ArcadeSlayerGuardianHit> Tick(ArcadeSlayerGuardianState s,ArcadeSlayerChallengeConfiguration c,
            float seconds,float bossX,float bossY,float targetX,float targetY,IReadOnlyList<ArcadeSlayerGuardianTarget> targets,float bodyRadius,
            Func<float,float,float,float,float> obstacleFraction=null)
        {
            if(s==null||c==null||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<0)throw new ArgumentException("Guardian时钟无效");
            var hits=new List<ArcadeSlayerGuardianHit>();if(seconds==0)return hits;
            s.Elapsed+=seconds;s.Snapshots.Clear();
            for(int i=s.Areas.Count-1;i>=0;i--)
            {
                var a=s.Areas[i];float active=Math.Min(seconds,a.Remaining);a.Remaining-=seconds;a.UntilTick-=active;
                if(active>0&&a.UntilTick<=0){foreach(var t in targets)if(InArea(t,a,bodyRadius))hits.Add(new ArcadeSlayerGuardianHit(t.Id,c.BurnDamage));a.UntilTick=c.BurnInterval;}
                if(a.Remaining<=0)s.Areas.RemoveAt(i);
            }
            long cycle=(long)Math.Floor(s.Elapsed/c.AttackPeriod);float phase=(float)(s.Elapsed-cycle*c.AttackPeriod);int kind=(int)(cycle%3);
            if(s.Cycle!=cycle){s.Cycle=cycle;s.OriginX=bossX;s.OriginY=bossY;s.TargetX=targetX;s.TargetY=targetY;}
            float dx=s.TargetX-s.OriginX,dy=s.TargetY-s.OriginY,length=(float)Math.Sqrt(dx*dx+dy*dy);
            if(length<.0001f){dx=0;dy=1;}else{dx/=length;dy/=length;}
            if(phase<c.AttackWarning)
            {
                s.Snapshots.Add(new ArcadeContentAreaSnapshot(-1,kind==0?13:kind==1?15:14,
                    kind==1?s.TargetX:s.OriginX,kind==1?s.TargetY:s.OriginY,kind==0?c.HammerRadius:kind==1?c.FireballRadius:c.FlameRadius,true,
                    s.OriginX+dx*c.FlameLength,s.OriginY+dy*c.FlameLength,kind==2));
            }
            else if(s.AttackCycle!=cycle)
            {
                s.AttackCycle=cycle;
                if(kind==0)
                {foreach(var t in targets)if(DistanceToSegment(t.X,t.Y,s.OriginX,s.OriginY,s.OriginX,s.OriginY)<=c.HammerRadius+bodyRadius)hits.Add(new ArcadeSlayerGuardianHit(t.Id,int.MaxValue,true));}
                else if(kind==1)s.Balls.Add(new ArcadeSlayerGuardianBall{Id=s.NextId++,X=s.OriginX,Y=s.OriginY,TargetX=s.TargetX,TargetY=s.TargetY});
                else
                {
                    var a=new ArcadeSlayerGuardianArea{Id=s.NextId++,Kind=14,X=s.OriginX,Y=s.OriginY,EndX=s.OriginX+dx*c.FlameLength,EndY=s.OriginY+dy*c.FlameLength,
                        Segment=true,Radius=c.FlameRadius,Remaining=c.BurnSeconds,UntilTick=c.BurnInterval};
                    s.Areas.Add(a);foreach(var t in targets)if(InArea(t,a,bodyRadius))hits.Add(new ArcadeSlayerGuardianHit(t.Id,c.BurnDamage));
                }
            }
            for(int i=s.Balls.Count-1;i>=0;i--)
            {
                var b=s.Balls[i];float bx=b.TargetX-b.X,by=b.TargetY-b.Y,dist=(float)Math.Sqrt(bx*bx+by*by),travel=Math.Min(dist,c.FireballSpeed*seconds);
                float ex=dist<.0001f?b.X:b.X+bx/dist*travel,ey=dist<.0001f?b.Y:b.Y+by/dist*travel;
                float fraction=obstacleFraction?.Invoke(b.X,b.Y,ex,ey)??1;bool explode=travel>=dist;
                foreach(var t in targets)if(DistanceToSegment(t.X,t.Y,b.X,b.Y,ex,ey)<=bodyRadius){explode=true;}
                if(fraction<1){ex=b.X+(ex-b.X)*fraction;ey=b.Y+(ey-b.Y)*fraction;explode=true;}
                b.X=ex;b.Y=ey;
                if(explode)
                {
                    foreach(var t in targets)if(DistanceToSegment(t.X,t.Y,ex,ey,ex,ey)<=c.FireballRadius+bodyRadius)hits.Add(new ArcadeSlayerGuardianHit(t.Id,c.FireballDamage));
                    s.Areas.Add(new ArcadeSlayerGuardianArea{Id=s.NextId++,Kind=15,X=ex,Y=ey,Radius=c.FireballRadius,Remaining=c.BurnSeconds,UntilTick=c.BurnInterval});s.Balls.RemoveAt(i);
                }
                else s.Snapshots.Add(new ArcadeContentAreaSnapshot(b.Id,15,b.X,b.Y,bodyRadius,false));
            }
            foreach(var a in s.Areas)s.Snapshots.Add(new ArcadeContentAreaSnapshot(a.Id,a.Kind,a.X,a.Y,a.Radius,false,a.EndX,a.EndY,a.Segment));
            return hits;
        }
    }
}

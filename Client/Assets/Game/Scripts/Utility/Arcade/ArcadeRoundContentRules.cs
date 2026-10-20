using System;
using System.Collections.Generic;

namespace FirepowerLoop.Arcade
{
    /// <summary>只读权威危险区域快照，Id 在地图代次内稳定；表现端不裁决伤害。</summary>
    public readonly struct ArcadeContentAreaSnapshot
    {
        public readonly int Id, Kind;
        public readonly float X, Y, Radius;
        public readonly bool Warning, Segment;
        public readonly float EndX, EndY;
        public ArcadeContentAreaSnapshot(int id, int kind, float x, float y, float radius, bool warning, float endX = 0, float endY = 0, bool segment = false)
        { Id=id;Kind=kind;X=x;Y=y;Radius=radius;Warning=warning;EndX=endX;EndY=endY;Segment=segment; }
    }
    /// <summary>双端同源的不可变内容参数，战斗数值全部来自轮级配置。</summary>
    public readonly struct ArcadeContentTuning
    {
        /// <summary>特殊敌人、环境事件与 Boss 的稳定配置编号。</summary>
        public readonly int SpecialKind, EventKind, BossKind;
        /// <summary>比例、倍率、周期秒、预警秒、强度、半径米及移动米每秒。</summary>
        public readonly float SpecialRatio, EnemyHealthScale, EnemySpeedScale, SkillPeriod, SkillWarning,
            SkillMagnitude, EventPeriod, EventWarning, EventRadius, EventDamage, EventMoveSpeed;
        public ArcadeContentTuning(int specialKind, int eventKind, int bossKind, float specialRatio,
            float enemyHealthScale, float enemySpeedScale, float skillPeriod, float skillWarning,
            float skillMagnitude, float eventPeriod, float eventWarning, float eventRadius,
            float eventDamage, float eventMoveSpeed)
        {
            SpecialKind = specialKind; EventKind = eventKind; BossKind = bossKind;
            SpecialRatio = specialRatio; EnemyHealthScale = enemyHealthScale; EnemySpeedScale = enemySpeedScale;
            SkillPeriod = skillPeriod; SkillWarning = skillWarning; SkillMagnitude = skillMagnitude;
            EventPeriod = eventPeriod; EventWarning = eventWarning; EventRadius = eventRadius;
            EventDamage = eventDamage; EventMoveSpeed = eventMoveSpeed;
        }
    }

    /// <summary>地图级活动时钟和每目标接触冷却；暂停不推进，长帧不补发多次伤害。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeContentClock
    {
        /// <summary>本地图目标下一次可命中时刻，切图随拥有者释放。</summary>
        private readonly Dictionary<long, double> nextHits = new Dictionary<long, double>();
        public double Elapsed { get; private set; }
        /// <summary>仅正有限活动步长推进；暂停或坏输入均不改变时钟。</summary>
        public void Advance(float delta, bool paused)
        { if (!paused && ArcadeRoundContentRules.Finite(delta) && delta > 0) Elapsed += delta; }
        /// <summary>接触确认后调用；同目标同 Tick 至多一次，不跨目标共用冷却。</summary>
        public bool TryHit(long targetId, float interval)
        {
            if (!ArcadeRoundContentRules.Finite(interval) || interval <= 0) return false;
            if (nextHits.TryGetValue(targetId, out double next) && Elapsed < next) return false;
            nextHits[targetId] = Elapsed + interval;
            return true;
        }
        /// <summary>实体销毁或身份复用前清理冷却。</summary>
        public void ForgetTarget(long targetId) { nextHits.Remove(targetId); }
    }

    /// <summary>低多边形轮廓零件：Cube/Sphere/Capsule/Cylinder，Y 为高度，颜色为 RRGGBBAA。</summary>
    public readonly struct ArcadeContentVisualPart
    {
        public readonly string Primitive;
        public readonly float X, Y, Z, ScaleX, ScaleY, ScaleZ;
        public readonly uint Color;
        public ArcadeContentVisualPart(string primitive, float x, float y, float z, float sx, float sy, float sz, uint color)
        { Primitive = primitive; X = x; Y = y; Z = z; ScaleX = sx; ScaleY = sy; ScaleZ = sz; Color = color; }
    }
    /// <summary>每次返回独立零件数组；调用者持有实际网格、材质与地图根生命周期。</summary>
    public readonly struct ArcadeContentVisualProfile
    {
        public readonly string Name;
        public readonly ArcadeContentVisualPart[] Parts;
        public ArcadeContentVisualProfile(string name, ArcadeContentVisualPart[] parts) { Name = name; Parts = parts; }
    }

    /// <summary>双端共用的纯确定性规则，不写血量、不生成实体、不拥有 Unity 生命周期。</summary>
    public static class ArcadeRoundContentRules
    {
        /// <summary>吸物使用双端一致的限幅直线运动；返回是否到达桶口，长帧不会越过桶心。</summary>
        public static bool PullToBarrel(float x,float y,float barrelX,float barrelY,float speed,float seconds,float mouthRadius,out float nextX,out float nextY)
        {
            nextX=x;nextY=y;
            if(!Finite(x)||!Finite(y)||!Finite(barrelX)||!Finite(barrelY)||!Finite(speed)||!Finite(seconds)||speed<=0||seconds<=0)return false;
            double dx=barrelX-x,dy=barrelY-y,distance=Math.Sqrt(dx*dx+dy*dy);
            double travel=Math.Min(distance,(double)speed*seconds);
            if(distance>0){nextX=x+(float)(dx/distance*travel);nextY=y+(float)(dy/distance*travel);}
            return distance-travel<=mouthRadius;
        }
        internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Time(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;
        /// <summary>只在配置接入边界调用；零内容允许零时长，已启用内容必须完整有效。</summary>
        public static void Validate(ArcadeContentTuning p)
        {
            foreach (float v in new[] {p.SpecialRatio,p.EnemyHealthScale,p.EnemySpeedScale,p.SkillPeriod,p.SkillWarning,
                p.SkillMagnitude,p.EventPeriod,p.EventWarning,p.EventRadius,p.EventDamage,p.EventMoveSpeed})
                if (!Finite(v) || v < 0) throw new ArgumentException("内容参数必须为非负有限值");
            if (p.SpecialKind < 0 || p.SpecialKind > 17 || p.EventKind < 0 || p.EventKind > 10 || p.BossKind < 0 || p.BossKind > 4 ||
                p.SpecialRatio > 1 || p.EventDamage > 1 ||
                ((p.SpecialKind != 0 || p.BossKind != 0) && (p.EnemyHealthScale <= 0 || p.EnemySpeedScale <= 0 ||
                    p.SkillPeriod <= 0 || p.SkillWarning <= 0 || p.SkillWarning >= p.SkillPeriod || p.SkillMagnitude <= 0)) ||
                (p.EventKind != 0 && (p.EventPeriod <= 0 || p.EventWarning <= 0 || p.EventWarning >= p.EventPeriod || p.EventRadius <= 0)))
                throw new ArgumentException("内容编号、倍率、周期或预警无效");
        }
        /// <summary>0 预警安全、1 活动、-1 无效；周期起点开始预警，活动窗口为 [warning, period)。</summary>
        public static int Phase(double elapsed, float period, float warning)
        {
            if (!Time(elapsed) || !Finite(period) || !Finite(warning) || period <= 0 || warning < 0 || warning >= period) return -1;
            return elapsed % period < warning ? 0 : 1;
        }
        /// <summary>基础移速已由 EnemySpeedScale 修正；此处只返回技能的额外倍率。</summary>
        public static float SpecialMotion(int kind, double elapsed, double lastHitAt, ArcadeContentTuning p)
        {
            if (kind == 3) return lastHitAt >= 0 && elapsed >= lastHitAt && elapsed-lastHitAt < p.SkillWarning ? 0 : p.SkillMagnitude;
            if (kind == 4 || kind == 11) return IsUnderground(kind, elapsed, p) ? 0 : p.SkillMagnitude;
            if (kind == 5 || kind == 10 || kind == 14 || kind == 17)
                return Phase(elapsed,p.SkillPeriod,p.SkillWarning)==1 ? p.SkillMagnitude : 1;
            return 1;
        }
        /// <summary>潜地或地下电杆在预警阶段不可移动或受击，出土阶段进入攻击窗口。</summary>
        public static bool IsUnderground(int kind, double elapsed, ArcadeContentTuning p)
            => (kind == 4 || kind == 11) && Phase(elapsed,p.SkillPeriod,p.SkillWarning)==0;
        /// <summary>重甲空降、重型及红色精英预警时举甲，活动时暴露弱点。</summary>
        public static bool IsArmored(int kind, double elapsed, ArcadeContentTuning p)
            => (kind == 8 || kind == 13 || kind == 16 || kind == 17) && Phase(elapsed,p.SkillPeriod,p.SkillWarning)==0;
        /// <summary>伤害入口使用此倍率，潜地免伤与护甲减伤不能由表现端决定。</summary>
        public static float IncomingDamageScale(int kind, double elapsed, ArcadeContentTuning p)
            => IsUnderground(kind,elapsed,p) ? 0 : IsArmored(kind,elapsed,p) ? 1/Math.Max(1,p.SkillMagnitude) : 1;
        /// <summary>Disciple 与高级祭司给范围内友军增益，距离由权威位置计算。</summary>
        public static float AllyBuff(int kind, float distance, ArcadeContentTuning p)
            => (kind==7 || kind==12) && Finite(distance) && distance>=0 && distance<=p.EventRadius ? Math.Max(1,p.SkillMagnitude) : 1;
        /// <summary>爆桶载体与毒性敌人的死亡领域范围，生成和寿命由现有战斗拥有者负责。</summary>
        public static float DeathAreaRadius(int kind, ArcadeContentTuning p) => kind==1 || kind==15 ? p.EventRadius : 0;
        /// <summary>孢子或寄生范围减速；强度大于一时使用倒数，保证不会反向移动或加速。</summary>
        public static float SporeSpeedScale(bool touching, ArcadeContentTuning p) => touching ? 1/Math.Max(1,p.SkillMagnitude) : 1;
        /// <summary>幽灵沿直线往返，涡流和冰风暴绕圆，其余事件保持配置锚点；不使用随机数。</summary>
        public static void EventCenter(int kind, double elapsed, float originX, float originY, float travelRadius, float moveSpeed, out float x, out float y)
        {
            x=originX; y=originY;
            if (!Time(elapsed) || !Finite(travelRadius) || !Finite(moveSpeed) || travelRadius<=0 || moveSpeed<0) return;
            if(kind==2) { double d=elapsed*moveSpeed % (travelRadius*4d); x+=(float)(d<=travelRadius*2d ? d-travelRadius : travelRadius*3d-d); }
            else if(kind==6) { double angle=elapsed*moveSpeed/travelRadius; x+=(float)Math.Cos(angle)*travelRadius; y+=(float)Math.Sin(angle)*travelRadius; }
        }
        /// <summary>圆接触包含边界和目标半径；非有限坐标或负半径不产生接触。</summary>
        public static bool TouchesCircle(float px, float py, float radius, float cx, float cy, float areaRadius)
            => TouchesSegment(px,py,radius,cx,cy,cx,cy,areaRadius);
        /// <summary>线段胶囊检测，包含端帽；零长线段退化成圆，避免激光端点漏伤。</summary>
        public static bool TouchesSegment(float px, float py, float radius, float ax, float ay, float bx, float by, float halfWidth)
        {
            if(!Finite(px)||!Finite(py)||!Finite(ax)||!Finite(ay)||!Finite(bx)||!Finite(by)||!Finite(radius)||!Finite(halfWidth)||radius<0||halfWidth<0)return false;
            double dx=(double)bx-ax,dy=(double)by-ay,len=dx*dx+dy*dy;
            double t=len>0?Math.Max(0,Math.Min(1,(((double)px-ax)*dx+((double)py-ay)*dy)/len)):0;
            double ex=px-ax-t*dx,ey=py-ay-t*dy,r=(double)radius+halfWidth;
            return ex*ex+ey*ey<=r*r;
        }
        /// <summary>最大生命比例伤害，正伤害至少一点且不超过最大生命，坏输入返回零。</summary>
        public static int Damage(int maxHealth, float ratio)
        { if(maxHealth<=0||!Finite(ratio)||ratio<=0)return 0;return (int)Math.Min(maxHealth,Math.Max(1,Math.Round(maxHealth*(double)ratio))); }
        /// <summary>每秒保留系数的指数惯性；输入归一化，暂停原样保留速度，碰撞仍由现有移动裁决。</summary>
        public static void UpdateIceVelocity(float vx, float vy, float inputX, float inputY, float speed, float retentionPerSecond, float delta, bool paused, out float x, out float y)
        {
            x=vx;y=vy;
            if(paused||!Finite(delta)||delta<=0||!Finite(retentionPerSecond)||retentionPerSecond<0||retentionPerSecond>1||!Finite(speed)||speed<0||!Finite(inputX)||!Finite(inputY))return;
            double length=Math.Sqrt((double)inputX*inputX+(double)inputY*inputY),normalize=Math.Max(1,length);
            float keep=(float)Math.Pow(retentionPerSecond,delta);
            x=vx*keep+(float)(inputX/normalize)*speed*(1-keep); y=vy*keep+(float)(inputY/normalize)*speed*(1-keep);
        }
        /// <summary>追踪弹按弧度每秒限幅转向，保持速度；目标方向零长时保持方向。</summary>
        public static void SteerTowards(float vx, float vy, float targetX, float targetY, float turnRadiansPerSecond, float delta, out float x, out float y)
        {
            x=vx;y=vy;
            if(!Finite(vx)||!Finite(vy)||!Finite(targetX)||!Finite(targetY)||!Finite(turnRadiansPerSecond)||!Finite(delta)||turnRadiansPerSecond<0||delta<=0||(targetX==0&&targetY==0))return;
            double angle=Math.Atan2(vy,vx),diff=Math.Atan2(targetY,targetX)-angle;
            diff=Math.Atan2(Math.Sin(diff),Math.Cos(diff));
            angle+=Math.Max(-turnRadiansPerSecond*(double)delta,Math.Min(turnRadiansPerSecond*(double)delta,diff));
            double speed=Math.Sqrt((double)vx*vx+(double)vy*vy);x=(float)(Math.Cos(angle)*speed);y=(float)(Math.Sin(angle)*speed);
        }
        /// <summary>0 冲锋、1 扇形弹、2 区域喷发、3 追踪齐射；Boss 按周期轮换自身攻击集合。</summary>
        public static int BossAttack(int kind, double elapsed, float period)
        { if(kind<1||kind>4||!Time(elapsed)||!Finite(period)||period<=0)return -1;int count=kind==1?2:kind==4?4:3;return (int)(Math.Floor(elapsed/period)%count); }

        private static ArcadeContentVisualPart P(string primitive,float x,float y,float z,float sx,float sy,float sz,uint color)
            => new ArcadeContentVisualPart(primitive,x,y,z,sx,sy,sz,color);
        /// <summary>每种特殊敌人具有不同结构轮廓；尺寸仅为美术相对尺寸，不参与碰撞及数值。</summary>
        public static ArcadeContentVisualProfile GetVisualProfile(int specialKind, int bossKind)
        {
            const uint bone=0xC9C7AEFF, dark=0x354050FF, red=0xCC4237FF, toxic=0x7ABB39FF, glow=0x88DDEEFF;
            if(bossKind!=0)
            {
                switch(bossKind)
                {
                    case 1:return new ArcadeContentVisualProfile("Guardian",new[]{P("Cube",0,1,0,1.5f,2,1,bone),P("Cube",-1.1f,1,0,.6f,2,1.8f,dark),P("Cylinder",1.1f,1,0,.5f,1.6f,.5f,glow)});
                    case 2:return new ArcadeContentVisualProfile("ZRex",new[]{P("Capsule",0,1,0,1.8f,1.5f,2.5f,red),P("Cube",0,2,1.6f,1.3f,1,1.5f,bone),P("Capsule",0,.8f,-2,.6f,.6f,2.5f,dark),P("Cube",0,1.8f,2.3f,1.1f,.3f,.6f,red)});
                    case 3:return new ArcadeContentVisualProfile("MegaAbomination",new[]{P("Sphere",0,1,0,2.7f,2.2f,2,toxic),P("Sphere",-.8f,2,0,1,1,1,bone),P("Sphere",.8f,2,0,1,1,1,red),P("Capsule",-1.7f,.8f,0,1,1.8f,1,dark),P("Capsule",1.7f,.8f,0,1,1.8f,1,dark)});
                    case 4:return new ArcadeContentVisualProfile("Papaback",new[]{P("Sphere",0,1.4f,0,2.5f,2.4f,1.7f,dark),P("Cube",0,2.6f,.4f,1.5f,1.2f,1.2f,red),P("Capsule",-1.9f,.9f,0,1.1f,2,1.1f,bone),P("Capsule",1.9f,.9f,0,1.1f,2,1.1f,bone),P("Cylinder",0,2.1f,-1,.8f,1.6f,.8f,glow)});
                    default:throw new ArgumentOutOfRangeException(nameof(bossKind));
                }
            }
            switch(specialKind)
            {
                case 0:return new ArcadeContentVisualProfile("None",new ArcadeContentVisualPart[0]);
                case 1:return new ArcadeContentVisualProfile("BarrelCarrier",new[]{P("Capsule",0,.7f,0,.7f,1.2f,.6f,bone),P("Cylinder",0,1,-.5f,.9f,.7f,.9f,red)});
                case 2:return new ArcadeContentVisualProfile("Crawler",new[]{P("Capsule",0,.3f,0,.8f,.4f,1.2f,bone),P("Sphere",0,.4f,.7f,.5f,.5f,.5f,dark),P("Cube",-.6f,.2f,.3f,.7f,.2f,.2f,bone),P("Cube",.6f,.2f,.3f,.7f,.2f,.2f,bone)});
                case 3:return new ArcadeContentVisualProfile("Mannequin",new[]{P("Cube",0,1,0,.45f,1.3f,.3f,red),P("Sphere",0,1.9f,0,.45f,.45f,.45f,bone),P("Cube",-.5f,1.2f,0,.7f,.16f,.16f,bone)});
                case 4:return new ArcadeContentVisualProfile("Burrower",new[]{P("Sphere",0,.35f,0,1.1f,.7f,1.1f,dark),P("Cube",0,.4f,.6f,.8f,.3f,.7f,bone)});
                case 5:return new ArcadeContentVisualProfile("Hound",new[]{P("Cube",0,.5f,0,.6f,.7f,1.3f,red),P("Capsule",0,.9f,.6f,.5f,.5f,.8f,dark),P("Cube",0,.2f,.3f,.9f,.4f,.2f,bone),P("Cube",0,.2f,-.4f,.9f,.4f,.2f,bone)});
                case 6:return new ArcadeContentVisualProfile("Spider",new[]{P("Sphere",0,.35f,0,1.1f,.5f,.8f,dark),P("Cube",0,.2f,.5f,2,.15f,.15f,red),P("Cube",0,.2f,-.5f,2,.15f,.15f,red),P("Cube",0,.3f,0,2.5f,.15f,.15f,bone)});
                case 7:return new ArcadeContentVisualProfile("Disciple",new[]{P("Capsule",0,1,0,.8f,1.4f,.6f,dark),P("Sphere",0,1.9f,0,.5f,.5f,.5f,glow),P("Cylinder",.7f,1,0,.15f,1.2f,.15f,glow)});
                case 8:return new ArcadeContentVisualProfile("Mangler",new[]{P("Cube",0,1,0,1.1f,1.5f,.8f,dark),P("Cylinder",.8f,.9f,.3f,.6f,.7f,.6f,red),P("Cube",-.8f,1,0,.4f,1,.4f,bone)});
                case 9:return new ArcadeContentVisualProfile("Parasite",new[]{P("Sphere",0,1.3f,0,.5f,.6f,.8f,toxic),P("Cube",-.7f,1.5f,0,1,.1f,.6f,bone),P("Cube",.7f,1.5f,0,1,.1f,.6f,bone)});
                case 10:return new ArcadeContentVisualProfile("Ghoul",new[]{P("Capsule",0,.8f,0,.5f,1.3f,.5f,dark),P("Capsule",-.5f,.6f,.3f,.2f,1,.2f,red),P("Capsule",.5f,.6f,.3f,.2f,1,.2f,red)});
                case 11:return new ArcadeContentVisualProfile("Pole",new[]{P("Cylinder",0,.9f,0,.35f,1.8f,.35f,dark),P("Sphere",0,1.9f,0,.8f,.8f,.8f,glow),P("Cube",0,1.4f,0,1.3f,.2f,.2f,bone)});
                case 12:return new ArcadeContentVisualProfile("ElderDisciple",new[]{P("Capsule",0,1.1f,0,1,1.7f,.7f,dark),P("Sphere",0,2.2f,0,.7f,.7f,.7f,glow),P("Cylinder",-.8f,1.5f,0,.15f,1.5f,.15f,glow),P("Cylinder",.8f,1.5f,0,.15f,1.5f,.15f,glow)});
                case 13:return new ArcadeContentVisualProfile("ArmoredDrop",new[]{P("Cube",0,1,0,1,1.4f,1,dark),P("Cube",0,1.1f,.7f,1.3f,1.6f,.3f,bone),P("Sphere",0,2.2f,0,1.4f,.3f,1.4f,red)});
                case 14:return new ArcadeContentVisualProfile("Amalgam",new[]{P("Sphere",0,1,0,1.4f,1.5f,1.2f,bone),P("Sphere",.7f,1.5f,.3f,.7f,.8f,.7f,red),P("Capsule",-1,.8f,0,.4f,2,.4f,dark),P("Capsule",1,.8f,0,.7f,1.2f,.7f,bone)});
                case 15:return new ArcadeContentVisualProfile("Toxic",new[]{P("Sphere",0,.8f,0,1.1f,1.3f,1,toxic),P("Cylinder",-.5f,1.4f,-.3f,.3f,.8f,.3f,glow),P("Cylinder",.5f,1.4f,-.3f,.3f,.8f,.3f,glow)});
                case 16:return new ArcadeContentVisualProfile("Abomination",new[]{P("Sphere",0,1,0,1.8f,1.7f,1.5f,toxic),P("Sphere",-.5f,1.9f,.3f,.7f,.7f,.7f,bone),P("Sphere",.5f,1.9f,.3f,.7f,.7f,.7f,bone),P("Cube",0,1,.8f,.6f,.6f,.6f,glow)});
                case 17:return new ArcadeContentVisualProfile("RedMangler",new[]{P("Cube",0,1,0,1.3f,1.5f,.9f,red),P("Cylinder",.9f,1,.4f,.8f,1,.8f,dark),P("Cube",-.9f,1,0,.4f,1.4f,.7f,bone),P("Cylinder",0,2,0,.4f,.5f,.4f,glow)});
                default:throw new ArgumentOutOfRangeException(nameof(specialKind));
            }
        }
    }
}

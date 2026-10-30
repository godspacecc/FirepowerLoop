using System;
namespace FirepowerLoop.Arcade
{
    /// <summary>普通敌人的已校验入口走廊，不改变玩家边界或特殊登场。</summary>
    public static class ArcadeOutsideRouteRules
    {
        /// <summary>沿入口主轴外延；完整圆避开真实障碍，已在场外的入口不再延长。</summary>
        public static bool TryStart(ArcadeRoundLayoutParameters layout,int entryIndex,float halfWidth,float halfHeight,float radius,out ArcadeOutsideEntryState state)
        {
            state=default;
            if(layout==null||layout.ArenaConfigNumber==1||layout.ArenaConfigNumber==21||layout.OutsideEntryDistance<=0||entryIndex<0||entryIndex>=layout.Entries.Count||
                halfWidth<=0||halfHeight<=0||radius<=0)return false;
            var entry=layout.Entries[entryIndex];
            if(!layout.IsWalkable(entry.X,entry.Y,radius,halfWidth,halfHeight))return false;
            bool horizontal=Math.Abs(entry.X)/halfWidth>Math.Abs(entry.Y)/halfHeight;
            float x=horizontal?Math.Sign(entry.X)*(halfWidth+layout.OutsideEntryDistance):entry.X;
            float z=horizontal?entry.Y:Math.Sign(entry.Y)*(halfHeight+layout.OutsideEntryDistance);
            foreach(var obstacle in layout.Obstacles)
                if(!IsPlayerMouthCap(layout,obstacle)&&Intersects(x,z,entry.X,entry.Y,obstacle,radius))return false;
            return ArcadeOutsideEntryRules.TryStart(x,z,entry.X,entry.Y,out state);
        }
        /// <summary>旧顺序与挑战按真实阻挡寻找合法边缘走廊，先尝试原出生方向，再扫描四边；不忽略任何实体。</summary>
        public static bool TryStartFallback(ArcadeRoundLayoutParameters tuning,float preferredX,float preferredZ,float halfWidth,float halfHeight,
            float radius,System.Collections.Generic.IReadOnlyList<ArcadeLayoutObstacle> obstacles,out ArcadeOutsideEntryState state)
        {
            state=default;
            if(tuning==null||tuning.OutsideEntryDistance<=0||halfWidth<=radius*2||halfHeight<=radius*2)return false;
            int first=Math.Abs(preferredX)/halfWidth>Math.Abs(preferredZ)/halfHeight?(preferredX<0?0:1):(preferredZ<0?2:3);
            for(int n=0;n<4;n++)
            {
                int side=(first+n)%4;bool horizontal=side<2;float bound=horizontal?halfWidth:halfHeight;
                float lateral=horizontal?halfHeight:halfWidth;float preferred=horizontal?preferredZ:preferredX;
                int count=(int)Math.Ceiling((lateral-radius*2)*2/(radius*2));
                for(int j=-1;j<=count;j++)
                {
                    float lane=j<0?Math.Max(-lateral+radius*2,Math.Min(lateral-radius*2,preferred)):
                        -lateral+radius*2+j*(lateral-radius*2)*2/Math.Max(1,count);
                    float sign=side==0||side==2?-1:1;
                    float tx=horizontal?sign*(bound-radius*2):lane,tz=horizontal?lane:sign*(bound-radius*2);
                    float sx=horizontal?sign*(bound+tuning.OutsideEntryDistance):lane,sz=horizontal?lane:sign*(bound+tuning.OutsideEntryDistance);
                    bool blocked=false;
                    foreach(var o in obstacles)if(Intersects(sx,sz,tx,tz,o,radius)){blocked=true;break;}
                    if(!blocked&&ArcadeOutsideEntryRules.TryStart(sx,sz,tx,tz,out state))return true;
                }
            }
            return false;
        }
        /// <summary>真正原地/潜地/空降登场保留；背桶、疾跑等行走身份不在排除名单。</summary>
        public static bool HasSpecialArrival(int kind,int phase,bool boss,bool dock,bool island)
            =>phase!=0||boss||dock||island||kind==4||kind==11||kind==13;
        /// <summary>仅放行已经由 EnemyMouths 验证的三张地图入口薄挡条；不放行实体墙或普通 MovementOnly 障碍。</summary>
        public static bool IsPlayerMouthCap(ArcadeRoundLayoutParameters layout,ArcadeLayoutObstacle o)
        {
            if(o.BlocksProjectiles)return false;
            bool horizontal=Near(o.HalfWidth,.2f),vertical=Near(o.HalfHeight,.2f);
            if(layout.ArenaConfigNumber==23)
                return horizontal&&Near(Math.Abs(o.X),26.9f)&&Near(o.Y,0)&&Near(o.HalfHeight,4.2f)||vertical&&Near(Math.Abs(o.Y),26.9f)&&Near(o.X,0)&&Near(o.HalfWidth,4.2f);
            if(layout.ArenaConfigNumber==24)
                return horizontal&&Near(Math.Abs(o.X),27.9f)&&Near(o.Y,0)&&Near(o.HalfHeight,5)||vertical&&Near(Math.Abs(o.Y),27.9f)&&Near(o.X,0)&&Near(o.HalfWidth,5);
            if(layout.ArenaConfigNumber==25)
                return horizontal&&Near(Math.Abs(o.X),25)&&Near(Math.Abs(o.Y),9)&&Near(o.HalfHeight,5)||vertical&&Near(Math.Abs(o.Y),23)&&Near(Math.Abs(o.X),11)&&Near(o.HalfWidth,5.5f);
            return false;
        }
        /// <summary>连续线段与膨胀矩形相交；端点和中段都不能穿实体。</summary>
        public static bool Intersects(float ax,float az,float bx,float bz,ArcadeLayoutObstacle o,float radius)
        {
            double enter=0,exit=1;
            return Slab(ax,bx-ax,o.X-o.HalfWidth-radius,o.X+o.HalfWidth+radius,ref enter,ref exit)&&
                Slab(az,bz-az,o.Y-o.HalfHeight-radius,o.Y+o.HalfHeight+radius,ref enter,ref exit)&&enter<exit;
        }
        /// <summary>单轴区间裁切。</summary>
        private static bool Slab(double start,double delta,double min,double max,ref double enter,ref double exit)
        {if(Math.Abs(delta)<1e-12)return start>min&&start<max;double a=(min-start)/delta,b=(max-start)/delta;enter=Math.Max(enter,Math.Min(a,b));exit=Math.Min(exit,Math.Max(a,b));return enter<exit;}
        /// <summary>白名单采用毫米级容差，配置变化需要重新核验。</summary>
        private static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
    }
}

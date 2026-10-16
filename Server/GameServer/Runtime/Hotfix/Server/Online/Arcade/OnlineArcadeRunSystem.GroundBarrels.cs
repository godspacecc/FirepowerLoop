using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>预置桶是地图对象，快照只投影仍存活索引，不进入敌人数量与自动事件时钟。</summary>
        private static void AppendGroundBarrelSnapshots(OnlineArcadeBattleRuntime b,OnlineArcadeBattleInfo snapshot)
        {
            if(b.RoundLayout==null||b.IsChallenge)return;
            for(int index=0;index<b.RoundLayout.Barrels.Count;index++)
            {
                if(b.DestroyedBarrelIds.Contains(index))continue;
                var point=b.RoundLayout.Barrels[index];var area=OnlineArcadeContentAreaInfo.Create();
                area.Id=-10000-index;area.Kind=11;area.X=point.X;area.Y=point.Y;area.Radius=ProjectileHitRadius;
                snapshot.ContentAreas.Add(area);
            }
        }
        /// <summary>圆实体真实首次交点；弹丸整帧越过圆心也能命中，起点在圆内视为立即接触。</summary>
        private static bool GroundCircleEntry(float ax,float ay,float bx,float by,float cx,float cy,float radius,out float entry)
        {
            double dx=(double)bx-ax,dy=(double)by-ay,ox=(double)ax-cx,oy=(double)ay-cy;
            double c=ox*ox+oy*oy-radius*(double)radius;
            if(c<=0){entry=0;return true;}
            double a=dx*dx+dy*dy,b=ox*dx+oy*dy,disc=b*b-a*c;
            if(a<=0||disc<0){entry=0;return false;}
            double t=(-b-Math.Sqrt(disc))/a;entry=(float)t;return t>=0&&t<=1;
        }
        /// <summary>只接受玩家实际武器投射物；墙、活敌人和箱体先命中时不能越过它们射桶。</summary>
        private static bool TryHitGroundBarrel(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,
            OnlineArcadeProjectileRuntime shot,float ax,float ay,float bx,float by)
        {
            if(!shot.FromPlayerWeapon||!(shot.RemainingSeconds>0)||shot.OwnerPlayerId<=0||b.RoundLayout==null||b.IsChallenge||
                !b.Players.TryGetValue(shot.OwnerPlayerId,out var owner)||owner.InTreasureRoom)return false;
            int selected=-1;float nearest=float.MaxValue;
            for(int i=0;i<b.RoundLayout.Barrels.Count;i++)
            {
                if(b.DestroyedBarrelIds.Contains(i))continue;var point=b.RoundLayout.Barrels[i];
                if(GroundCircleEntry(ax,ay,bx,by,point.X,point.Y,ProjectileHitRadius,out float entry)&&entry<nearest)
                {nearest=entry;selected=i;}
            }
            if(selected<0)return false;
            float blockerEntry=nearest;bool blocked=false;
            foreach(var obstacle in EffectiveObstacles(b))
                if(TryGetSegmentObstacleEntry(ax,ay,bx,by,0,obstacle,out float entry)&&entry<=blockerEntry)
                {blockerEntry=entry;blocked=true;}
            foreach(var enemy in b.Enemies.Values)
                if(enemy.CurrentHealth>0&&!shot.HitEnemyIds.Contains(enemy.RuntimeId)&&
                    GroundCircleEntry(ax,ay,bx,by,enemy.PositionX,enemy.PositionZ,
                        ServerConfig.Instance.ArcadeRule.EnemyContactRadius+ProjectileHitRadius,out float entry)&&entry<=blockerEntry)
                {blockerEntry=entry;blocked=true;}
            foreach(var crate in b.Crates.Values)
                if(crate.Health>0&&GroundCircleEntry(ax,ay,bx,by,crate.Definition.X,crate.Definition.Y,
                    crate.Definition.Radius+ProjectileHitRadius,out float entry)&&entry<=blockerEntry)
                {blockerEntry=entry;blocked=true;}
            if(blocked)
            {
                // 截在首接触内一毫米，再交还既有墙/箱/敌命中入口；不能仅拒绝桶却把弹丸留在遮挡后方。
                double dx=(double)bx-ax,dy=(double)by-ay,length=Math.Sqrt(dx*dx+dy*dy);
                float fraction=Math.Min(1,blockerEntry+(float)(.001/Math.Max(.001,length)));
                shot.PositionX=ax+(bx-ax)*fraction;shot.PositionZ=ay+(by-ay)*fraction;
                return false;
            }
            // 稳定身份先注销，再执行范围后果；重复Tick、弹丸或快照不会重放爆炸。
            if(!b.DestroyedBarrelIds.Add(selected))return false;
            shot.PositionX=ax+(bx-ax)*nearest;shot.PositionZ=ay+(by-ay)*nearest;
            var center=b.RoundLayout.Barrels[selected];var p=ContentTuning(b);
            DamageContentEnemies(actor,b,center.X,center.Y,p.EventRadius,p.EventDamage,shot.OwnerPlayerId);
            foreach(var player in b.Players.Values.Where(v=>v.Alive&&!v.InTreasureRoom).ToArray())
                if(ArcadeRoundContentRules.TouchesCircle(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,center.X,center.Y,p.EventRadius))
                    actor.ContentDamagePlayer(b,player,ArcadeRoundContentRules.Damage(player.MaxHealth,p.EventDamage));
            // 玩家与敌人已在命中时裁决；短领域只显示爆炸，不在宝藏返回或恢复后补伤。
            AddContentZone(b,3,center.X,center.Y,center.X,center.Y,p.EventRadius,0,p.SkillWarning,0,false,false);
            return true;
        }
        /// <summary>玩家武器长帧也截在首面墙，不能本帧越墙后下一帧从墙后射桶。</summary>
        private static bool ClipPlayerShotToWall(OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime shot,float ax,float ay)
        {
            if(!shot.FromPlayerWeapon||shot.OwnerPlayerId<=0)return false;
            float first=float.MaxValue;
            foreach(var wall in EffectiveObstacles(b))
                if(TryGetSegmentObstacleEntry(ax,ay,shot.PositionX,shot.PositionZ,0,wall,out float entry)&&entry<first)first=entry;
            if(first==float.MaxValue)return false;
            shot.PositionX=ax+(shot.PositionX-ax)*first;shot.PositionZ=ay+(shot.PositionZ-ay)*first;return true;
        }
    }
}

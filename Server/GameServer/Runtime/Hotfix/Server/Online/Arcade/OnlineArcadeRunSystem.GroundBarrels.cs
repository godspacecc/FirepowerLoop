using System;
using System.Linq;
using System.Collections.Generic;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>预置桶与动态桶共用稳定身份；动态索引从预置数组之后开始且本地图内不复用。</summary>
        private static IEnumerable<KeyValuePair<int,ArcadeRegionPoint>> GroundBarrelPoints(OnlineArcadeBattleRuntime b)
        {
            for(int index=0;index<b.RoundLayout.Barrels.Count;index++)
                if(!b.DestroyedBarrelIds.Contains(index))yield return new KeyValuePair<int,ArcadeRegionPoint>(index,b.RoundLayout.Barrels[index]);
            foreach(var pair in b.DroppedBarrels.OrderBy(p=>p.Key))yield return pair;
        }

        /// <summary>服务器唯一推进载体放桶和吸物；全队共享删除，不发放奖励或保留已吞物品。</summary>
        private static void TickVortexBarrels(OnlineArcadeBattleRuntime b,float seconds)
        {
            if(!RoundContentActive(b)||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            var layout=b.RoundLayout;
            b.NextGroundBarrelId=Math.Max(b.NextGroundBarrelId,layout.Barrels.Count);
            foreach(var enemy in b.Enemies.Values.OrderBy(e=>e.RuntimeId))
                if(enemy.SpecialKind==1&&enemy.CurrentHealth>0&&b.ContentClock.Elapsed-enemy.ContentBornAt>=layout.BarrelDropSeconds)
                {
                    float x=enemy.PositionX,y=enemy.PositionZ;
                    ResolvePosition(ref x,ref y,x,y,ServerConfig.Instance.ArcadeRule.PlayerRadius,ServerConfig.Instance.ArcadeRule,EffectiveObstacles(b),IslandFor(b));
                    b.DroppedBarrels.Add(b.NextGroundBarrelId++,new ArcadeRegionPoint(x,y));enemy.SpecialKind=0;enemy.ContentWarning=false;
                }
            var barrels=GroundBarrelPoints(b).ToArray();if(barrels.Length==0)return;
            foreach(var drop in b.Drops.Values.ToArray())
            {
                if(drop.DeliveryRemainingSeconds>0)continue;
                var nearest=barrels.OrderBy(p=>DistanceSquared(p.Value.X,p.Value.Y,drop.PositionX,drop.PositionZ)).ThenBy(p=>p.Key).First();
                if(DistanceSquared(nearest.Value.X,nearest.Value.Y,drop.PositionX,drop.PositionZ)>layout.BarrelPullRadius*layout.BarrelPullRadius)continue;
                bool consumed=ArcadeRoundContentRules.PullToBarrel(drop.PositionX,drop.PositionZ,nearest.Value.X,nearest.Value.Y,
                    layout.BarrelPullSpeed,seconds,ProjectileHitRadius,out float x,out float y);
                drop.PositionX=x;drop.PositionZ=y;if(consumed)b.Drops.Remove(drop.RuntimeId);
            }
        }
        /// <summary>预置桶是地图对象，快照只投影仍存活索引，不进入敌人数量与自动事件时钟。</summary>
        private static void AppendGroundBarrelSnapshots(OnlineArcadeBattleRuntime b,OnlineArcadeBattleInfo snapshot)
        {
            if(b.RoundLayout==null||b.IsChallenge)return;
            foreach(var pair in GroundBarrelPoints(b))
            {
                var point=pair.Value;var area=OnlineArcadeContentAreaInfo.Create();
                area.Id=-10000-pair.Key;area.Kind=pair.Key<b.RoundLayout.Barrels.Count?11:13;area.X=point.X;area.Y=point.Y;area.Radius=ProjectileHitRadius;
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
            foreach(var pair in GroundBarrelPoints(b))
            {
                var point=pair.Value;
                if(GroundCircleEntry(ax,ay,bx,by,point.X,point.Y,ProjectileHitRadius,out float entry)&&entry<nearest)
                {nearest=entry;selected=pair.Key;}
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
            shot.PositionX=ax+(bx-ax)*nearest;shot.PositionZ=ay+(by-ay)*nearest;
            int remaining=(b.GroundBarrelHealth.TryGetValue(selected,out int health)?health:b.RoundLayout.BarrelHealth)-Math.Max(0,shot.Damage);
            if(remaining>0){b.GroundBarrelHealth[selected]=remaining;return true;}
            var center=selected<b.RoundLayout.Barrels.Count?b.RoundLayout.Barrels[selected]:b.DroppedBarrels[selected];
            // 身份先注销再执行范围后果；动态桶不会写入预置桶恢复索引。
            if(selected<b.RoundLayout.Barrels.Count)b.DestroyedBarrelIds.Add(selected);else b.DroppedBarrels.Remove(selected);
            b.GroundBarrelHealth.Remove(selected);var p=ContentTuning(b);
            DamageContentExplosionEnemies(actor,b,center.X,center.Y,p.EventRadius,p.EventDamage,shot.OwnerPlayerId);
            // 玩家与敌人已在命中时裁决；短领域只显示爆炸，不在宝藏返回或恢复后补伤。
            AddContentZone(b,ArcadeRoundContentRules.BarrelExplosionKind,center.X,center.Y,center.X,center.Y,p.EventRadius,0,p.SkillWarning,0,false,false);
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

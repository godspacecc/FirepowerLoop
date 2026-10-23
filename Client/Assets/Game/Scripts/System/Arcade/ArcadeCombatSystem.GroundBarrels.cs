using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>第二关每批通道首只实际出敌后补一个静止桶，复用地图桶的命中、吸物和清理。</summary>
        private void SpawnPassageBarrel(ArcadeCombatRuntime r)
        {
            var layout=RoundLayout(r);if(layout==null||layout.ArenaConfigNumber!=21)return;
            var occupied=r.GroundBarrels.Values.Select(p=>new ArcadeRegionPoint(p.x,p.y))
                .Append(new ArcadeRegionPoint(r.PlayerPosition.x,r.PlayerPosition.y));
            if(!layout.TryChoosePassageBarrelPoint(ArenaHalfWidth,ArenaHalfHeight,catalog.Rule.PlayerRadius,occupied,random,out var point))return;
            r.NextGroundBarrelId=Math.Max(r.NextGroundBarrelId,layout.Barrels.Count);
            r.GroundBarrels.Add(r.NextGroundBarrelId++,new Vector2(point.X,point.Y));
        }
        /// <summary>同一地图重复准备不补发，新地图与布局版本才建立自己的稳定索引。</summary>
        private void InitializeGroundBarrels(ArcadeCombatRuntime r)
        {
            var layout=RoundLayout(r);
            if(layout!=null && r.GroundBarrelMapId==layout.Id && r.GroundBarrelLayoutVersion==layout.Version)return;
            r.GroundBarrels.Clear();r.GroundBarrelHealth.Clear();r.DestroyedGroundBarrelIds.Clear();r.NextGroundBarrelId=layout?.Barrels.Count??0;
            r.GroundBarrelMapId=layout?.Id;r.GroundBarrelLayoutVersion=layout?.Version ?? 0;
            if(layout==null)return;
            for(int index=0;index<layout.Barrels.Count;index++)
                r.GroundBarrels.Add(index,new Vector2(layout.Barrels[index].X,layout.Barrels[index].Y));
        }

        /// <summary>恢复前先拒绝未知身份，不能在验证失败前清理正在显示的现场。</summary>
        private static void ValidateGroundBarrelCheckpoint(ArcadeRoundLayoutParameters layout,ArcadeRoundCheckpoint checkpoint)
        {
            if(checkpoint.MapId!=layout.Id || checkpoint.LayoutVersion!=layout.Version || checkpoint.DestroyedBarrelIds==null ||
                checkpoint.DestroyedBarrelIds.Any(id=>id<0 || id>=layout.Barrels.Count) ||
                checkpoint.DestroyedBarrelIds.Distinct().Count()!=checkpoint.DestroyedBarrelIds.Count)
                throw new InvalidOperationException("轮前地面桶身份与当前布局不兼容，原存档已保留");
        }

        /// <summary>轮前回滚使用该轮快照，不把活动中桶收益单独提交成新的角色节点。</summary>
        private void RestoreGroundBarrels(ArcadeCombatRuntime r,ArcadeRoundCheckpoint checkpoint)
        {
            var layout=RoundLayout(r);ValidateGroundBarrelCheckpoint(layout,checkpoint);
            // 显式恢复可以回到轮初，区别于同一持续活动轮的重复初始化。
            r.GroundBarrelMapId=null;InitializeGroundBarrels(r);
            foreach(int id in checkpoint.DestroyedBarrelIds){r.GroundBarrels.Remove(id);r.DestroyedGroundBarrelIds.Add(id);}
        }

        /// <summary>桶不参与角色碰撞与清场计数，静态表现身份在本地图内固定。</summary>
        private void AppendGroundBarrelSnapshots(ArcadeCombatRuntime r)
        {
            if(r.InTreasureRoom || r.IslandTransitionPending || r.IslandRoomTransitionPending || RoundLayout(r)?.Id!=r.GroundBarrelMapId)return;
            foreach(var pair in r.GroundBarrels.OrderBy(pair=>pair.Key))
                r.ContentPresentationSnapshots.Add(new ArcadeContentAreaSnapshot(-10000-pair.Key,pair.Key<RoundLayout(r).Barrels.Count?11:13,pair.Value.x,pair.Value.y,ProjectileHitRadius,false));
        }

        /// <summary>活动战斗中只放一次桶；桶与掉落实体均由本地图拥有，暂停和奖励房不推进。</summary>
        private void TickVortexBarrels(ArcadeCombatRuntime r,float seconds)
        {
            if(!RoundContentActive(r)||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            var layout=RoundLayout(r);
            foreach(var enemy in r.Enemies.Values.OrderBy(e=>e.Id))
                if(enemy.ContentSpecialKind==1&&enemy.Health>0&&r.ContentClock.Elapsed-enemy.ContentBornAt>=layout.BarrelDropSeconds)
                {
                    int id=r.NextGroundBarrelId++;r.GroundBarrels.Add(id,ContentPosition(r,enemy.Position));
                    if(enemy.CarriedBarrelHealth>0)r.GroundBarrelHealth[id]=enemy.CarriedBarrelHealth;
                    enemy.CarriedBarrelHealth=0;
                    enemy.ContentSpecialKind=0;enemy.ContentWarning=false;
                }
            foreach(var drop in r.Drops.Values.ToArray())
            {
                if(drop.DeliveryRemainingSeconds>0)continue;
                var nearest=r.GroundBarrels.OrderBy(b=>(b.Value-drop.Position).sqrMagnitude).ThenBy(b=>b.Key).FirstOrDefault();
                if(r.GroundBarrels.Count==0||(nearest.Value-drop.Position).sqrMagnitude>layout.BarrelPullRadius*layout.BarrelPullRadius)continue;
                bool consumed=ArcadeRoundContentRules.PullToBarrel(drop.Position.x,drop.Position.y,nearest.Value.x,nearest.Value.y,
                    layout.BarrelPullSpeed,seconds,ProjectileHitRadius,out float x,out float y);
                drop.Position=new Vector2(x,y);
                if(consumed){r.Drops.Remove(drop.Id);view?.RemoveDrop(drop.Id);runtimePool.Return(drop);}
            }
        }

        /// <summary>玩家子弹按真实线段查找最近目标；墙、敌人与箱体先于远桶阻挡。</summary>
        private bool TryHitGroundBarrel(ArcadeCombatRuntime r,ArcadeProjectileRuntime projectile,Vector2 from,Vector2 to)
        {
            if(projectile.Hostile || !projectile.FromPlayerWeapon || !RoundContentActive(r) || r.GroundBarrels.Count==0)return false;
            float first=1;int barrelId=-1;
            foreach(var pair in r.GroundBarrels.OrderBy(pair=>pair.Key))
                if(TryGroundBarrelEntry(from,to,pair.Value,ProjectileHitRadius+projectile.EvolutionHitRadius,out float entry) && (barrelId<0 || entry<first))
                { first=entry;barrelId=pair.Key; }
            if(barrelId<0)return false;
            bool blocked=false;
            foreach(var obstacle in ProjectileObstaclesFor(r))
                if(TryGetSegmentObstacleEntry(from,to,projectile.EvolutionHitRadius,obstacle,out var entry) && entry<=first)
                {first=entry;blocked=true;}
            foreach(var enemy in r.Enemies.Values)
                if(enemy.Health>0 && !projectile.HitEnemyIds.Contains(enemy.Id) && TryGroundBarrelEntry(from,to,enemy.Position,
                    catalog.Rule.EnemyContactRadius+ProjectileHitRadius+projectile.EvolutionHitRadius,out var entry) && entry<=first)
                {first=entry;blocked=true;}
            foreach(var crate in r.Crates.Values)
            {
                var c=crate.Definition;
                if(TryGroundBarrelEntry(from,to,new Vector2(c.X,c.Y),c.Radius+ProjectileHitRadius+projectile.EvolutionHitRadius,out var entry) && entry<=first)
                {first=entry;blocked=true;}
            }
            // 更近的正式目标继续交给原命中入口；微小内移避免浮点端点落在接触边界之外。
            projectile.Position=Vector2.Lerp(from,to,Mathf.Min(1,first+.00001f));
            if(blocked)return false;
            int remaining=(r.GroundBarrelHealth.TryGetValue(barrelId,out int health)?health:RoundLayout(r).BarrelHealth)-Math.Max(0,projectile.Damage);
            if(remaining<=0)DetonateGroundBarrel(r,barrelId);else r.GroundBarrelHealth[barrelId]=remaining;
            // 与既有箱体命中保持一致：消费弹丸，但爆炸武器自身的半径和伤害不能丢失。
            if(!r.CompletionSubmitted && catalog!=null && this.GetModel<ArcadeCombatModel>().Runtime==r && IsExplosiveBehavior(projectile.Behavior))
            {
                PlayWeaponProjectileExplosion(projectile);
                DamageEnemiesInProjectileExplosion(r,projectile);
            }
            return true;
        }

        /// <summary>先移除桶身份再裁决范围伤害；只伤敌人，动态桶不污染预置桶恢复索引。</summary>
        private void DetonateGroundBarrel(ArcadeCombatRuntime r,int id)
        {
            if(!RoundContentActive(r) || !r.GroundBarrels.TryGetValue(id,out var position))return;
            r.GroundBarrels.Remove(id);r.GroundBarrelHealth.Remove(id);
            if(id<RoundLayout(r).Barrels.Count)r.DestroyedGroundBarrelIds.Add(id);
            var p=ContentTuning(r);
            AddContentArea(r,ArcadeRoundContentRules.BarrelExplosionKind,position,position,p.EventRadius,0,p.SkillWarning,0,false,false);
            DamageContentExplosionEnemies(r,position,p.EventRadius,p.EventDamage);
        }

        /// <summary>线段圆的首次进入比例；端点跳过桶也能命中，起点在圆内视为立即接触。</summary>
        private static bool TryGroundBarrelEntry(Vector2 from,Vector2 to,Vector2 center,float radius,out float entry)
        {
            entry=0;var offset=from-center;double c=(double)offset.x*offset.x+(double)offset.y*offset.y-radius*radius;
            if(c<=0)return true;
            var delta=to-from;double a=(double)delta.x*delta.x+(double)delta.y*delta.y;if(a<=0)return false;
            double b=2*((double)offset.x*delta.x+(double)offset.y*delta.y),discriminant=b*b-4*a*c;
            if(discriminant<0)return false;
            double hit=(-b-Math.Sqrt(discriminant))/(2*a);if(hit<0 || hit>1)return false;
            entry=(float)hit;return true;
        }
    }
}

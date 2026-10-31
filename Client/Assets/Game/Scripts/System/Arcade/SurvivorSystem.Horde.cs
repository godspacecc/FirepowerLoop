using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>开发期性能标记，区分尸潮调度、角色展开及整段幸存者逻辑，避免把编辑器尖峰误算为刷怪。</summary>
        private static readonly Unity.Profiling.ProfilerMarker HordeTickMarker=new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts,"FirepowerLoop.Horde.Tick");
        private static readonly Unity.Profiling.ProfilerMarker HordeWakeMarker=new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts,"FirepowerLoop.Horde.Wake");
        private static readonly Unity.Profiling.ProfilerMarker SurvivorFrameMarker=new Unity.Profiling.ProfilerMarker(Unity.Profiling.ProfilerCategory.Scripts,"FirepowerLoop.Survivor.Frame");
        /// <summary>只保存尚未创建成功的出生请求；成功创建后全部进入持续追击，不按距离休眠。</summary>
        private readonly SurvivorHordeState horde=new SurvivorHordeState();
        private readonly List<SurvivorActivationRequest> activationRequests=new List<SurvivorActivationRequest>();
        private bool hordeInitialized;
        private float hordeWaveClock;
        private int nearbySpawnSequence,hordeWaveSide;
        private int hordeWarmIndex;
        /// <summary>固定格宽只影响索引性能，不改变来自配置的命中半径。</summary>
        private readonly SurvivorEnemySpatialIndex enemySpatial=new SurvivorEnemySpatialIndex(4);
        private readonly List<int> enemyCandidates=new List<int>();
        private readonly List<int> expiredBullets=new List<int>();
        /// <summary>爬出胶囊超出平面圆形体积，保留小规模精确候选，不能用猜测半径漏掉手臂。</summary>
        private readonly List<int> groundEntryCandidates=new List<int>();
        private float maximumEnemyRadius;
        private bool spatialFrameReady;
        private int spatialPopulationCount;

        /// <summary>局结束清理所有模拟层，不触碰死亡与奖励事务。</summary>
        private void ResetHorde()
        {
            horde.Clear();activationRequests.Clear();enemySpatial.Clear();enemyCandidates.Clear();expiredBullets.Clear();groundEntryCandidates.Clear();
            hordeInitialized=spatialFrameReady=false;hordeWaveClock=0;nearbySpawnSequence=hordeWaveSide=hordeWarmIndex=0;
        }
        /// <summary>入口调用，每帧最多预热一个角色；失败仍属于加载事务，不交接半准备的战场。</summary>
        public bool TryPrepareHordePresentation(out bool complete,out string error)
        {
            complete=false;error=null;if(enemyRoster==null){error="尸潮敌池尚未准备";return false;}
            if(hordeWarmIndex>=enemyRoster.Entries.Count){complete=true;return true;}
            int desired=Math.Max(config.HordeActivationBudget,config.HordeGridSize);
            if(!view.TryPrewarmSurvivorEnemy(enemyRoster.Entries[hordeWarmIndex].EnemyId,desired,out bool warmed,out error))return false;
            if(warmed)hordeWarmIndex++;
            complete=hordeWarmIndex>=enemyRoster.Entries.Count;
            if(complete)
            {
                navigation.SetTarget(player);pickupNavigation.SetTarget(player);
                foreach(var field in sizedNavigation.Values)field.SetTarget(player);
            }
            return true;
        }

        /// <summary>保守验证所有已配置体型与玩家拾取通路，防止远场展开在围栏内。</summary>
        private bool HordeFloor(float x,float y,bool spawning=true)
        {
            var p=new Vector2(x,y);
            if((spawning&&Vector2.Distance(p,player)<config.MinimumSpawnDistance)||!navigation.IsReachable(p))return false;
            foreach(var field in sizedNavigation.Values)if(!field.IsReachable(p))return false;
            if(pickupNavigation==null)pickupNavigation=new SurvivorNavigation(HalfWidth,HalfHeight,catalog.Rule.PlayerRadius,obstacles);
            pickupNavigation.SetTarget(player);return pickupNavigation.IsReachable(p);
        }
        /// <summary>敌种在生成群时抽取，后续时间解锁不修改已生成群的身份。</summary>
        private (string EnemyId,float Speed) HordeChoice(int region)
        {
            var choice=enemyRoster.Select(Model.State.ElapsedSeconds,(float)Math.Min(enemyRandom.NextDouble(),.99999994));
            return (choice.EnemyId,enemyConfigs[choice.EnemyId].MoveSpeed*choice.SpeedMultiplier);
        }
        /// <summary>地图分散铺怪只在首次有效战斗帧创建轻量记录；视野内不凭空补出站立敌人。</summary>
        private void InitializeHorde()
        {
            foreach(var field in sizedNavigation.Values)field.SetTarget(player);
            SurvivorHordeRules.Seed(horde,config.HordeGridSize,config.HordeGridSize,HalfWidth-2,HalfHeight-2,
                player.x,player.y,config.MinimumSpawnDistance,config.HordeSeedCount,config.HordeSpawnAttempts,unchecked((int)runId),
                (x,y)=>HordeFloor(x,y)&&view.IsArenaSpawnHidden(new Vector2(x,y),2),HordeChoice);
            hordeInitialized=true;hordeWaveClock=NextScatterInterval();
        }
        /// <summary>外围随机选择地图边缘方向，有限尝试；失败不积累集中补刷欠账。</summary>
        private void SpawnHordeWave()
        {
            int side=enemyRandom.Next(4);hordeWaveSide++;
            for(int i=0;i<config.HordeSpawnAttempts;i++)
            {
                float x=((float)enemyRandom.NextDouble()*2-1)*(HalfWidth-3),y=((float)enemyRandom.NextDouble()*2-1)*(HalfHeight-3);
                if(side==0)x=-HalfWidth+3;else if(side==1)y=HalfHeight-3;else if(side==2)x=HalfWidth-3;else y=-HalfHeight+3;
                if(!HordeFloor(x,y)||!view.IsArenaSpawnHidden(new Vector2(x,y),2))continue;
                var choice=HordeChoice(side);horde.Add(side,choice.EnemyId,x,y,choice.Speed,1);return;
            }
        }
        /// <summary>表现失败不扣人数、不丢失原个体；暂停并给出真实错误。</summary>
        private bool WakeHordeEnemy(ArcadeEnemyRuntime enemy)
        {
            using var sample=HordeWakeMarker.Auto();
            if(!view.TryWakeSurvivorEnemy(enemy.Id,enemy.ConfigId,enemy.Position,enemy.Health,enemy.MaxHealth,enemy.AttackSequence,out var error))
            {LastError=error??"尸潮角色展开失败";Model.Paused=true;input.Disable();Publish();return false;}
            enemies.Add(enemy.Id,enemy);view.SetEnemyScale(enemy.Id,Shape(enemy.ConfigId)?.VisualScale??Vector3.one);return true;
        }
        /// <summary>当前模式已取消属性时间成长；仍从配置计算出生属性，不改变共用敌人定义。</summary>
        private ArcadeEnemyRuntime CreateHordeEnemy(SurvivorActivationRequest request,Vector2 position)
        {
            var shape=Shape(request.EnemyId);var definition=enemyConfigs[request.EnemyId];double minutes=Model.State.ElapsedSeconds/60;
            int health=(int)Math.Min(int.MaxValue,Math.Round(definition.MaxHP*shape.HealthMultiplier*(1+minutes*config.EnemyHealthGrowthPerMinute),MidpointRounding.AwayFromZero));
            return new ArcadeEnemyRuntime{Id=checked(++identity),ConfigId=request.EnemyId,AttackKind=definition.AttackKind,Position=position,Health=health,MaxHealth=health,
                Damage=(int)Math.Min(int.MaxValue,definition.AttackDamage*shape.DamageMultiplier*(1+minutes*config.EnemyDamageGrowthPerMinute)),Speed=request.Speed*(float)(1+minutes*config.EnemySpeedGrowthPerMinute)};
        }
        /// <summary>按出生预算分帧创建全图敌人；距离和相机只用于安全出生，不限制追击资格。</summary>
        private bool TickHorde(float dt)
        {
            using var sample=HordeTickMarker.Auto();
            if(config.HordeGridSize<=0)return false;
            if(!view.TryGetSurvivorViewBounds(out _))
            {
                if(hordeInitialized){LastError="尸潮相机范围暂不可用，请返回菜单重新进入";Model.Paused=true;input.Disable();Publish();return true;}
                return false;
            }
            foreach(var field in sizedNavigation.Values)field.SetTarget(player);
            if(!hordeInitialized)InitializeHorde();
            hordeWaveClock-=dt;
            if(hordeWaveClock<=0){SpawnHordeWave();hordeWaveClock=NextScatterInterval();}
            // 所有地图内出生请求均按预算推进，不等待玩家进入准备带，也不再转入休眠层。
            horde.Reserve(-HalfWidth,-HalfHeight,HalfWidth,HalfHeight,config.HordeActivationBudget,activationRequests);
            foreach(var request in activationRequests)
            {
                // 出生时已经分散，不再把共用中心展开为紧密圆环。
                var p=new Vector2(request.X,request.Y);
                if(!HordeFloor(p.x,p.y,false)){horde.Defer(request.Id);continue;}
                var enemy=CreateHordeEnemy(request,p);if(!WakeHordeEnemy(enemy))return true;
                horde.Commit(request.Id);
            }
            return true;
        }
        /// <summary>外围逐只错时补充，失败不欠账；平均间隔由权威配置控制。</summary>
        private float NextScatterInterval()=>config.HordeWaveSeconds*(.65f+(float)enemyRandom.NextDouble()*.7f);

        /// <summary>移动结束后重建近场索引，弹丸与自动瞄准只查询有完整战斗资格的实体。</summary>
        private void RebuildHordeSpatial()
        {
            enemySpatial.Clear();groundEntryCandidates.Clear();maximumEnemyRadius=0;
            foreach(var enemy in enemies.Values){enemySpatial.Add(enemy.Id,enemy.Position.x,enemy.Position.y);maximumEnemyRadius=Mathf.Max(maximumEnemyRadius,EnemyRadius(enemy));if(enemy.GroundEntry.Active)groundEntryCandidates.Add(enemy.Id);}
            spatialFrameReady=true;spatialPopulationCount=enemies.Count;
        }
        /// <summary>同帧复用移动后的快照；独立战斗入口或实体数量变化时先补建索引。</summary>
        private void EnsureHordeSpatial(){if(!spatialFrameReady||spatialPopulationCount!=enemies.Count)RebuildHordeSpatial();}
    }
}

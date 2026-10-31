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
        /// <summary>未展开敌群和已展开过的个体例外分开保存，后者完整保留血量及稳定ID。</summary>
        private readonly SurvivorHordeState horde=new SurvivorHordeState();
        private readonly Dictionary<int,ArcadeEnemyRuntime> dormantEnemies=new Dictionary<int,ArcadeEnemyRuntime>();
        private readonly List<SurvivorActivationRequest> activationRequests=new List<SurvivorActivationRequest>();
        private readonly List<int> hordeTransfers=new List<int>();
        private bool hordeInitialized;
        private float hordeFarClock,hordeWaveClock;
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
            horde.Clear();dormantEnemies.Clear();activationRequests.Clear();hordeTransfers.Clear();enemySpatial.Clear();enemyCandidates.Clear();expiredBullets.Clear();groundEntryCandidates.Clear();
            hordeInitialized=spatialFrameReady=false;hordeFarClock=hordeWaveClock=0;nearbySpawnSequence=hordeWaveSide=hordeWarmIndex=0;
        }
        /// <summary>入口调用，每帧最多预热一个角色；失败仍属于加载事务，不交接半准备的战场。</summary>
        public bool TryPrepareHordePresentation(out bool complete,out string error)
        {
            complete=false;error=null;if(enemyRoster==null){error="尸潮敌池尚未准备";return false;}
            if(hordeWarmIndex>=enemyRoster.Entries.Count){complete=true;return true;}
            int desired=Math.Max(config.HordeSeedCount,config.HordeWaveCount);
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
        private static Rect ExpandHordeBounds(Rect rect,float margin)=>Rect.MinMaxRect(rect.xMin-margin,rect.yMin-margin,rect.xMax+margin,rect.yMax+margin);

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
        /// <summary>地图铺怪只在首次有效战斗帧创建轻量记录；视野内不凭空补出站立敌人。</summary>
        private void InitializeHorde()
        {
            foreach(var field in sizedNavigation.Values)field.SetTarget(player);
            SurvivorHordeRules.Seed(horde,config.HordeGridSize,config.HordeGridSize,HalfWidth-2,HalfHeight-2,
                player.x,player.y,config.MinimumSpawnDistance,config.HordeSeedCount,config.HordeSpawnAttempts,unchecked((int)runId),
                (x,y)=>HordeFloor(x,y)&&view.IsArenaSpawnHidden(new Vector2(x,y),2),HordeChoice);
            hordeInitialized=true;hordeWaveClock=config.HordeWaveSeconds;
        }
        /// <summary>每潮轮换地图边缘方向，有限尝试；失败不积累集中补刷欠账。</summary>
        private void SpawnHordeWave()
        {
            int side=hordeWaveSide++%4;
            for(int i=0;i<config.HordeSpawnAttempts;i++)
            {
                float x=((float)enemyRandom.NextDouble()*2-1)*(HalfWidth-3),y=((float)enemyRandom.NextDouble()*2-1)*(HalfHeight-3);
                if(side==0)x=-HalfWidth+3;else if(side==1)y=HalfHeight-3;else if(side==2)x=HalfWidth-3;else y=-HalfHeight+3;
                if(!HordeFloor(x,y)||!view.IsArenaSpawnHidden(new Vector2(x,y),2))continue;
                var choice=HordeChoice(side);horde.Add(side,choice.EnemyId,x,y,choice.Speed,config.HordeWaveCount);return;
            }
        }
        /// <summary>远场沿共享导航低频前进，未准备好的新群在可见边界外等候展开。</summary>
        private (float X,float Y) MoveHordeGroup(int region,string enemyId,float x,float y,float distance)
        {
            float radius=catalog.Rule.EnemyContactRadius*(Shape(enemyId)?.BodyRadiusMultiplier??1);
            var field=sizedNavigation.TryGetValue(radius,out var sized)?sized:navigation;
            var next=field.Move(new Vector2(x,y),player,distance);
            return view.IsArenaSpawnHidden(next,2)?(next.x,next.y):(x,y);
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
        /// <summary>已有弹丸可能触及的角色不休眠，避免切层后弹丸穿过本应命中的敌人。</summary>
        private bool HordeProjectileThreat(ArcadeEnemyRuntime enemy)
        {
            foreach(var bullet in bullets.Values)
                if(Vector2.Distance(bullet.Position,enemy.Position)<=bullet.Velocity.magnitude*bullet.RemainingSeconds+EnemyRadius(enemy))return true;
            return false;
        }
        /// <summary>一帧只展开配置预算；先恢复带血量的个体，再展开尚未创建的群成员。</summary>
        private bool TickHorde(float dt)
        {
            using var sample=HordeTickMarker.Auto();
            if(config.HordeGridSize<=0)return false;
            if(!view.TryGetSurvivorViewBounds(out var visible))
            {
                if(hordeInitialized){LastError="尸潮相机范围暂不可用，请返回菜单重新进入";Model.Paused=true;input.Disable();Publish();return true;}
                return false;
            }
            foreach(var field in sizedNavigation.Values)field.SetTarget(player);
            if(!hordeInitialized)InitializeHorde();
            float margin=Mathf.Max(config.HordePrepareMargin,config.PlayerMoveSpeed*(float)(1+Bonus("MoveSpeed"))*config.HordeFarTickSeconds+2);
            var prepare=ExpandHordeBounds(visible,margin);var retire=ExpandHordeBounds(visible,Mathf.Max(config.HordeRetireMargin,margin+2));
            hordeFarClock+=dt;hordeWaveClock-=dt;
            if(hordeWaveClock<=0){SpawnHordeWave();hordeWaveClock=config.HordeWaveSeconds;}
            if(hordeFarClock>=config.HordeFarTickSeconds)
            {
                float elapsed=hordeFarClock;hordeFarClock=0;
                horde.Advance(elapsed,MoveHordeGroup);
                foreach(var enemy in dormantEnemies.Values)
                {
                    var next=NavigationFor(enemy).Move(enemy.Position,player,enemy.Speed*elapsed);
                    // 在准备带内停止低频移动，优先交回完整模拟，防止跨过玩家或穿过弹丸。
                    if(!prepare.Contains(enemy.Position))enemy.Position=next;
                }
                hordeTransfers.Clear();
                foreach(var enemy in enemies.Values)
                    if(!retire.Contains(enemy.Position)&&!enemy.GroundEntry.Active&&!enemy.MeleeWindupActive&&enemy.MeleeRecoveryRemaining<=0&&!HordeProjectileThreat(enemy))hordeTransfers.Add(enemy.Id);
                foreach(int id in hordeTransfers){var enemy=enemies[id];view.SuspendSurvivorEnemy(id);dormantEnemies.Add(id,enemy);enemies.Remove(id);}
            }
            int budget=config.HordeActivationBudget;hordeTransfers.Clear();
            foreach(var pair in dormantEnemies)
            {
                if(!prepare.Contains(pair.Value.Position))continue;
                // 相机突变导致已可见的旧个体必须恢复，不能变成隐形敌人；常规提前带受预算控制。
                if(hordeTransfers.Count>=budget&&!visible.Contains(pair.Value.Position))continue;
                hordeTransfers.Add(pair.Key);
            }
            foreach(int id in hordeTransfers)
            {
                if(!WakeHordeEnemy(dormantEnemies[id]))return true;
                dormantEnemies.Remove(id);budget=Math.Max(0,budget-1);
            }
            long visibleCount=horde.CountInBounds(visible.xMin,visible.yMin,visible.xMax,visible.yMax);
            // 可见性优先于预创建预算：极端相机突变的近场成本单列性能报告，不能靠藏怪制造流畅。
            if(visibleCount>0)horde.Reserve(visible.xMin,visible.yMin,visible.xMax,visible.yMax,checked((int)visibleCount),activationRequests);
            else horde.Reserve(prepare.xMin,prepare.yMin,prepare.xMax,prepare.yMax,budget,activationRequests);
            foreach(var request in activationRequests)
            {
                // 固定令牌生成小范围落点，避免整群共用一点；安全失败保留群人数等待后续帧。
                float angle=(float)(request.Id*2.399963229728653% (Math.PI*2));
                var p=new Vector2(request.X,request.Y)+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*1.4f;
                if(!HordeFloor(p.x,p.y,false))p=new Vector2(request.X,request.Y);
                if(!HordeFloor(p.x,p.y,false)){horde.Defer(request.Id);continue;}
                var enemy=CreateHordeEnemy(request,p);if(!WakeHordeEnemy(enemy))return true;
                horde.Commit(request.Id);
            }
            return true;
        }
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

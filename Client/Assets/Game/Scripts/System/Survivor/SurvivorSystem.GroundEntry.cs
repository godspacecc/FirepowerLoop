using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>独立出生随机流不改变敌种抽样；真实身体姿态按敌种缓存。</summary>
        private readonly System.Random groundRandom=new System.Random();
        private readonly Dictionary<string,ArcadeHitPoseSequence> groundHitPoses=new();
        private SurvivorNavigation pickupNavigation;
        /// <summary>有界抽样整段爬行通路，并同时检查玩家可达的经验落点。</summary>
        private bool TryGroundSpawn(float radius,out SurvivorGroundEntryState state)
        {
            state=default;
            if(navigation==null)navigation=new SurvivorNavigation(HalfWidth,HalfHeight,catalog.Rule.EnemyContactRadius,obstacles);
            navigation.SetTarget(player);
            if(pickupNavigation==null)pickupNavigation=new SurvivorNavigation(HalfWidth,HalfHeight,catalog.Rule.PlayerRadius,obstacles);
            pickupNavigation.SetTarget(player);
            if(!sizedNavigation.TryGetValue(radius,out var field))
            {field=Mathf.Approximately(radius,catalog.Rule.EnemyContactRadius)?navigation:new SurvivorNavigation(HalfWidth,HalfHeight,radius,obstacles);sizedNavigation[radius]=field;}
            field.SetTarget(player);
            for(int attempt=0;attempt<64;attempt++)
            {
                float angle=(float)groundRandom.NextDouble()*Mathf.PI*2;
                float range=config.MinimumSpawnDistance+1+(float)groundRandom.NextDouble()*8;
                var origin=player+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*range;
                var forward=(player-origin).normalized;
                var end=origin+forward*SurvivorGroundEntryRules.CrawlDistance(SurvivorGroundEntryRules.TotalSeconds);
                // 支撑手臂和翻土留出空间，不允许贴墙爬出；较大身体沿自身距离场验证。
                float clearance=Mathf.Max(radius,.9f*radius/catalog.Rule.EnemyContactRadius);
                if(Vector2.Distance(end,player)<config.MinimumSpawnDistance||!field.IsReachable(origin)||!field.IsReachable(end)||!field.CanTravel(origin,end)||!pickupNavigation.IsReachable(origin)||!pickupNavigation.IsReachable(end))continue;
                bool clear=true;
                foreach(var point in new[]{origin,end})
                {
                    if(Mathf.Abs(point.x)>HalfWidth-clearance||Mathf.Abs(point.y)>HalfHeight-clearance){clear=false;break;}
                    foreach(var obstacle in obstacles)if(Mathf.Abs(point.x-obstacle.Center.x)<obstacle.HalfExtents.x+clearance&&Mathf.Abs(point.y-obstacle.Center.y)<obstacle.HalfExtents.y+clearance){clear=false;break;}
                    if(!clear)break;
                }
                if(!clear)continue;
                state=new SurvivorGroundEntryState{Started=true,StartX=origin.x,StartZ=origin.y,ForwardX=forward.x,ForwardZ=forward.y};return true;
            }
            return false;
        }
        /// <summary>提前准备正式三体型的身体采样，避免首发弹丸才遇到缺失配置。</summary>
        private void PrepareGroundHitPoses()
        {
            var provider=this.GetUtility<IRuntimeConfigProvider>();groundHitPoses.Clear();
            foreach(var pair in enemyConfigs)
            {if(!provider.TryGetDockHitPose(pair.Value.PrefabAddress,out var poses,out var error))throw new InvalidOperationException(error);groundHitPoses.Add(pair.Key,poses);}
        }
        /// <summary>弹道与真实采样身体相交才命中；已站稳敌人继续既有平面战斗。</summary>
        private bool TryGroundBodySweep(ArcadeEnemyRuntime enemy,Vector2 from,Vector2 to,float height,out float fraction)
        {
            fraction=0;if(!groundHitPoses.TryGetValue(enemy.ConfigId,out var poses))throw new InvalidOperationException("幸存者爬出身体配置未准备："+enemy.ConfigId);
            var scale=Shape(enemy.ConfigId)?.VisualScale??Vector3.one;var state=enemy.GroundEntry;
            ArcadeHitPoint Local(Vector2 point){var d=point-enemy.Position;return new ArcadeHitPoint(d.x*state.ForwardZ-d.y*state.ForwardX,height-SurvivorGroundEntryRules.ExtraDepth(state.Age)*scale.y,d.x*state.ForwardX+d.y*state.ForwardZ);}
            ArcadeHitPoint Scale(ArcadeHitPoint p)=>new ArcadeHitPoint(p.X*scale.x,p.Y*scale.y,p.Z*scale.z);
            var a=Local(from);var b=Local(to);double first=double.PositiveInfinity;
            for(int i=0;i<poses.CapsulesPerFrame;i++)
            {
                var source=poses.Sample(SurvivorGroundEntryRules.ClipAge(state.Age),i);
                var capsule=new ArcadeHitCapsule(Scale(source.A),Scale(source.B),source.Radius*Math.Max(scale.x,Math.Max(scale.y,scale.z)));
                if(ArcadeHitGeometry.TrySweepSphere(a,b,0,capsule,out var hit))first=Math.Min(first,hit);
            }
            if(double.IsInfinity(first))return false;fraction=(float)first;return true;
        }
        private bool CanAimGroundBody(ArcadeEnemyRuntime enemy,Vector2 origin,float height)=>!enemy.GroundEntry.Active||TryGroundBodySweep(enemy,origin,enemy.Position+(enemy.Position-origin).normalized*3,height,out _);
    }
}

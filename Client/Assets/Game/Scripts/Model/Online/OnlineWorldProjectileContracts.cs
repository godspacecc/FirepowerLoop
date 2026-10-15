using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Game
{
    /// <summary>服务器弹体的不可变客户端快照，位置为地图本地米，时间为服务器毫秒。</summary>
    public readonly struct OnlineWorldProjectileSnapshot
    {
        public readonly long RuntimeId,OwnerUnitId,OwnerLifecycleRevision,AttackSequence,SpawnServerTime,SampleServerTime,StateRevision;
        public readonly string ProjectileConfigId;
        public readonly Vector3 Position,Direction;
        public readonly float Speed,RemainingDistance;
        public OnlineWorldProjectileSnapshot(ET.WorldProjectileInfo p)
        {RuntimeId=p.RuntimeId;OwnerUnitId=p.OwnerUnitId;OwnerLifecycleRevision=p.OwnerLifecycleRevision;AttackSequence=p.AttackSequence;ProjectileConfigId=p.ProjectileConfigId;Position=new Vector3(p.PositionX,p.PositionY,p.PositionZ);Direction=new Vector3(p.DirectionX,p.DirectionY,p.DirectionZ);Speed=p.Speed;RemainingDistance=p.RemainingDistance;SpawnServerTime=p.SpawnServerTime;SampleServerTime=p.SampleServerTime;StateRevision=p.StateRevision;}
        public Vector3 PositionAt(long serverTime)=>Position+Direction*Mathf.Min(RemainingDistance,Speed*Mathf.Max(0,(serverTime-SampleServerTime)/1000f));
    }
    /// <summary>按单弹体版本收敛消息，终止优先；全量快照建立版本下限，防止重连后晚到出生复活旧箭。</summary>
    public sealed class OnlineWorldProjectileState
    {
        private long worldId,fullRevision,latestRevision;
        private string mapId;
        private readonly Dictionary<long,OnlineWorldProjectileSnapshot> projectiles=new Dictionary<long,OnlineWorldProjectileSnapshot>();
        private readonly Dictionary<long,long> terminated=new Dictionary<long,long>();
        public IReadOnlyList<OnlineWorldProjectileSnapshot> Projectiles=>projectiles.Values.OrderBy(p=>p.RuntimeId).ToArray();
        public void ObserveWorld(long world,string map)
        {if(worldId==world&&mapId==map)return;worldId=world;mapId=map;fullRevision=latestRevision=0;projectiles.Clear();terminated.Clear();}
        public void ReplaceSnapshot(IReadOnlyList<OnlineWorldProjectileSnapshot> values,long revision)
        {
            if(revision<latestRevision)return;latestRevision=fullRevision=revision;projectiles.Clear();
            foreach(var p in values??Array.Empty<OnlineWorldProjectileSnapshot>())if(Valid(p)&&!terminated.ContainsKey(p.RuntimeId))projectiles[p.RuntimeId]=p;
            // 比全量快照更旧的消息由 fullRevision 拦截，已收敛的墓碑不必永久累积。
            terminated.Clear();
        }
        public void Apply(ET.G2C_WorldProjectilesChanged message)
        {
            if(message.WorldId!=worldId||message.MapId!=mapId)return;latestRevision=Math.Max(latestRevision,message.WorldRevision);
            foreach(var end in message.Terminations)
            {if(end.RuntimeId<=0)continue;terminated[end.RuntimeId]=Math.Max(end.StateRevision,terminated.TryGetValue(end.RuntimeId,out var r)?r:0);projectiles.Remove(end.RuntimeId);}
            foreach(var info in message.Projectiles)
            {
                var p=new OnlineWorldProjectileSnapshot(info);if(!Valid(p)||terminated.ContainsKey(p.RuntimeId))continue;
                if(projectiles.TryGetValue(p.RuntimeId,out var old)){if(old.StateRevision>=p.StateRevision)continue;}
                else if(message.WorldRevision<=fullRevision)continue;
                projectiles[p.RuntimeId]=p;
            }
        }
        public void PruneOwners(IReadOnlyList<OnlineWorldPlayerSnapshot> players)
        {
            foreach(var p in projectiles.Values.ToArray())
            {if(players.Any(a=>a.UnitId==p.OwnerUnitId&&!a.IsDead&&a.LifecycleRevision==p.OwnerLifecycleRevision))continue;projectiles.Remove(p.RuntimeId);terminated[p.RuntimeId]=long.MaxValue;}
        }
        private static bool Valid(OnlineWorldProjectileSnapshot p)=>p.RuntimeId>0&&p.StateRevision>0&&p.Speed>0&&p.RemainingDistance>=0&&Finite(p.Position)&&Finite(p.Direction)&&p.Direction.sqrMagnitude>.000001f&&!float.IsNaN(p.Speed)&&!float.IsInfinity(p.Speed)&&!float.IsNaN(p.RemainingDistance)&&!float.IsInfinity(p.RemainingDistance);
        private static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z);
    }
}

using System.Collections.Generic;
using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>按服务器样本时间外推联机箭矢姿态；服务器负责碰撞、伤害和回收，本系统只管理可见实例。</summary>
    public sealed class OnlineWorldProjectilePresentationSystem:AbstractSystem
    {
        private readonly HashSet<long> visible=new HashSet<long>();
        private long sampledAt;
        private float receivedAt;
        private IOnlineWorldProjectilePresentationPort port;
        protected override void OnInit(){port=this.GetUtility<IOnlineWorldProjectilePresentationPort>();LifeCycle.RegisterUpdate(Tick);}
        protected override void OnDeinit(){LifeCycle.UnregisterUpdate(Tick);port?.ClearOnlineProjectiles();visible.Clear();}
        private void Tick()
        {
            var map=this.GetModel<MapSceneModel>();var world=this.GetSystem<OnlineWorldSystem>().Snapshot;
            var root=map.CurrentHandle?.Context?.MapContentRoot;
            if(port==null)return;
            if(map.CurrentMode!=MapPreparationMode.OnlinePlayersOnly||map.Status.Value!=MapLoadStatus.Loaded||root==null||!root.activeInHierarchy||!world.HasWorld||map.CurrentMapId!=world.MapId)
            {port.ClearOnlineProjectiles();visible.Clear();sampledAt=0;return;}
            if(sampledAt!=world.ServerTime){sampledAt=world.ServerTime;receivedAt=Time.realtimeSinceStartup;}
            var now=sampledAt+(long)((Time.realtimeSinceStartup-receivedAt)*1000);
            var seen=new HashSet<long>();
            foreach(var p in world.Projectiles)
            {
                if(!this.GetUtility<IRuntimeConfigProvider>().TryGetProjectile(p.ProjectileConfigId,out var config,out _))continue;
                if(port.TrySetProjectile(p.RuntimeId,this.GetSystem<MapSceneSystem>().CurrentMapInstanceId,config.PrefabAddress,root.transform.TransformPoint(p.PositionAt(now)),root.transform.TransformVector(p.Direction).normalized,out _))seen.Add(p.RuntimeId);
            }
            foreach(var id in visible)if(!seen.Contains(id))port.RemoveProjectile(id);
            visible.Clear();visible.UnionWith(seen);
        }
    }
}

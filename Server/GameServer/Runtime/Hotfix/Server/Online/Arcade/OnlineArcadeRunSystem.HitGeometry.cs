using System;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>服务器读取同一正式模型采样，缺失配置抛出明确错误。</summary>
        private static ArcadeHitPoseSequence DockHitPose(OnlineArcadeEnemyRuntime enemy)
        {
            var definition=ServerConfig.Instance.GetEnemy(enemy.EnemyConfigId);
            if(!ServerConfig.Instance.TryGetDockHitPose("Prefabs/Characters/"+definition.PrefabName,out var pose,out var error))throw new InvalidOperationException(error);
            return pose;
        }
        /// <summary>码头实际高度扫掠，其他敌人保留既有二维圆。</summary>
        private static bool TryEnemySweep(OnlineArcadeEnemyRuntime enemy,float ax,float az,float bx,float bz,float legacyRadius,float bodyRadius,out float entry)
        {
            if(!enemy.DockSeaEntry.Active)return GroundCircleEntry(ax,az,bx,bz,enemy.PositionX,enemy.PositionZ,legacyRadius,out entry);
            var forward=ArcadeDockSeaEntryRules.Forward(enemy.DockSeaEntry);
            bool hit=DockHitPose(enemy).TrySweepSphere(enemy.DockSeaEntry.Age,new ArcadeHitPoint(ax,.75,az),new ArcadeHitPoint(bx,.75,bz),bodyRadius,
                enemy.PositionX,enemy.PositionZ,forward.X,forward.Y,out double fraction);
            entry=(float)fraction;return hit;
        }
        /// <summary>范围伤害按攻击球与实际身体相交；非码头保留平面范围。</summary>
        private static bool EnemyInHitSphere(OnlineArcadeEnemyRuntime enemy,float x,float z,float radius)
        {
            if(!enemy.DockSeaEntry.Active)return DistanceSquared(x,z,enemy.PositionX,enemy.PositionZ)<=radius*radius;
            var forward=ArcadeDockSeaEntryRules.Forward(enemy.DockSeaEntry);
            return DockHitPose(enemy).OverlapsSphere(enemy.DockSeaEntry.Age,new ArcadeHitPoint(x,.75,z),radius,enemy.PositionX,enemy.PositionZ,forward.X,forward.Y);
        }
        /// <summary>自动追踪按当前真实射线选取码头目标，不使用入场时间门槛。</summary>
        private static bool CanAimAtEnemy(OnlineArcadeEnemyRuntime enemy,float x,float z)
        {
            if(!enemy.CanBeAttacked)return false;
            if(!enemy.DockSeaEntry.Active)return true;
            float dx=enemy.PositionX-x,dz=enemy.PositionZ-z,length=MathF.Sqrt(dx*dx+dz*dz);
            if(length>0){dx/=length;dz/=length;}
            return TryEnemySweep(enemy,x,z,enemy.PositionX+dx*3,enemy.PositionZ+dz*3,0,0,out _);
        }
    }
}

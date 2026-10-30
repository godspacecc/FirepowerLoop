using System;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>码头身体必须来自正式模型采样；配置缺失中止查询并报告原因。</summary>
        private ArcadeHitPoseSequence DockHitPose(ArcadeEnemyRuntime enemy)
        {
            var provider=this.GetUtility<IRuntimeConfigProvider>();
            if(!provider.TryGetEnemy(enemy.ConfigId,out var definition,out var error))throw new InvalidOperationException(error);
            if(!provider.TryGetDockHitPose(definition.PrefabAddress,out var pose,out error))throw new InvalidOperationException(error);
            return pose;
        }
        /// <summary>码头使用实际高度的完整扫掠；其他敌人保留原二维圆判定。</summary>
        private bool TryEnemySweep(ArcadeEnemyRuntime enemy,Vector2 from,Vector2 to,float legacyRadius,float bodyRadius,out float entry)
        {
            if(!enemy.DockSeaEntry.Active)return TryGroundBarrelEntry(from,to,enemy.Position,legacyRadius,out entry);
            var forward=ArcadeDockSeaEntryRules.Forward(enemy.DockSeaEntry);
            bool hit=DockHitPose(enemy).TrySweepSphere(enemy.DockSeaEntry.Age,new ArcadeHitPoint(from.x,.75,from.y),new ArcadeHitPoint(to.x,.75,to.y),bodyRadius,
                enemy.Position.x,enemy.Position.y,forward.X,forward.Y,out double fraction);
            entry=(float)fraction;return hit;
        }
        /// <summary>范围攻击以现有攻击高度为球心，码头精查身体；正常敌人保持平面范围。</summary>
        private bool EnemyInHitSphere(ArcadeEnemyRuntime enemy,Vector2 center,float radius)
        {
            if(!enemy.DockSeaEntry.Active)return IsWithinExplosion(center,enemy.Position,radius);
            var forward=ArcadeDockSeaEntryRules.Forward(enemy.DockSeaEntry);
            return DockHitPose(enemy).OverlapsSphere(enemy.DockSeaEntry.Age,new ArcadeHitPoint(center.x,.75,center.y),radius,enemy.Position.x,enemy.Position.y,forward.X,forward.Y);
        }
        /// <summary>自动瞄准仅选择当前射线能接触的码头身体；不会凭入场年龄赋予免伤。</summary>
        private bool CanAimAtEnemy(ArcadeEnemyRuntime enemy,Vector2 origin)
        {
            if(!enemy.CanBeAttacked)return false;
            if(!enemy.DockSeaEntry.Active)return true;
            var direction=(enemy.Position-origin).normalized;
            return TryEnemySweep(enemy,origin,enemy.Position+direction*3,0,0,out _);
        }
    }
}

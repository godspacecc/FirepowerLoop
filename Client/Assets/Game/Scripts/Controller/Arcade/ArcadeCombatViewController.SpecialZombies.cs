using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>完整状态在普通追踪后投影，盾牌朝向始终由规则方向决定。</summary>
        public void SetSurvivorEnemySpecial(int id,string kind,Vector2 direction,bool warning,float progress,Vector2 target,float radius)
        {
            if(mapRoot==null)return;
            enemyPresentation?.SetSurvivorSpecial(id,kind,mapRoot.TransformDirection(World(direction,0)),warning,progress,radius);
        }
        /// <summary>将地图平面敌弹姿态转换到正式火球显示高度。</summary>
        public void SetSurvivorHostileProjectile(int id,Vector2 position,Vector2 direction)
        {if(mapRoot!=null)projectilePresentation?.SetSurvivorHostileProjectile(id,mapRoot.TransformPoint(World(position,1.1f)),mapRoot.TransformDirection(World(direction,0)));}
        /// <summary>转发权威敌弹移除。</summary>
        public void RemoveSurvivorHostileProjectile(int id)=>projectilePresentation?.RemoveSurvivorHostileProjectile(id);
        /// <summary>敌爆使用独立表现端口与独立容量。</summary>
        public void SetSurvivorEnemyBlast(int id,Vector2 position,float radius,float progress,int capacity)
        {if(mapRoot!=null)projectilePresentation?.SetSurvivorEnemyBlast(id,mapRoot.TransformPoint(World(position,0)),radius,progress,capacity);}
        /// <summary>转发敌爆结束，不接玩家强化池。</summary>
        public void RemoveSurvivorEnemyBlast(int id)=>projectilePresentation?.RemoveSurvivorEnemyBlast(id);
    }
}

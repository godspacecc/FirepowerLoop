using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        public void SetHerdActivityPaused(bool paused)
        { PauseHerdFeedback(paused);enemyPresentation?.SetHerdActivityPaused(paused); }
        /// <summary>主角和远端都消费当前地图的只读状态，异步实例晚到时仍读取最新姿态。</summary>
        public void SetPlayerHerdImpact(long playerId,ArcadeHerdImpactState state)
        {
            var direction=new Vector3(state.DirectionX,0,state.DirectionZ);if(mapRoot!=null)direction=mapRoot.TransformDirection(direction);
            if(playerId==0)playerMotor?.SetHerdImpact(state,direction);
            else if(remotePlayers.TryGetValue(playerId,out var entry))
            {entry.HerdImpact=state;entry.View?.Instance?.GetComponent<PlayerView>()?.SetHerdImpact(state,direction);}
        }
        public void SetEnemyHerdImpact(int id,ArcadeHerdImpactState state)
        {
            ObserveEnemyHerdImpact(id,state);
            var direction=new Vector3(state.DirectionX,0,state.DirectionZ);if(mapRoot!=null)direction=mapRoot.TransformDirection(direction);
            enemyPresentation?.SetHerdImpact(id,state,direction);
        }
    }
}

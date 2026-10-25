using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>仅21—25采用新场地规则；巨石资源绑定不改变旧出生、移动或传送门。</summary>
        private static ArcadeEarlyStageParameters EarlyStage(OnlineArcadeBattleRuntime b)=>b!=null&&b.ArenaConfigNumber>=21&&b.ArenaConfigNumber<=25&&ServerConfig.Instance.EarlyStages.TryGetValue(b.ArenaConfigNumber,out var stage)?stage:null;
        private static float ArenaHalfWidth(OnlineArcadeBattleRuntime b)=>EarlyStage(b)?.HalfWidth??ServerConfig.Instance.ArcadeRule.ArenaHalfWidth;
        private static float ArenaHalfHeight(OnlineArcadeBattleRuntime b)=>EarlyStage(b)?.HalfHeight??ServerConfig.Instance.ArcadeRule.ArenaHalfHeight;

        /// <summary>新地图按短步推进实际阻挡，不能从一侧平台长帧越水到另一侧；旧图保持原入口。</summary>
        private static void ResolveBattlePosition(OnlineArcadeBattleRuntime b,ref float x,ref float z,float previousX,float previousZ,float radius,
            IReadOnlyList<ArcadeObstacleConfig> obstacles,ArcadeIslandRegion island=null)
        {
            var rule=ServerConfig.Instance.ArcadeRule;var stage=EarlyStage(b);
            if(stage==null){ResolvePosition(ref x,ref z,previousX,previousZ,radius,rule,obstacles,island);return;}
            float dx=x-previousX,dz=z-previousZ;int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt((double)dx*dx+(double)dz*dz)/.2));
            x=previousX;z=previousZ;
            for(int i=0;i<steps;i++)
            {
                float px=x,pz=z;x+=dx/steps;z+=dz/steps;
                ResolvePositionWithMargin(ref x,ref z,px,pz,radius,rule,obstacles,island,0,stage.HalfWidth,stage.HalfHeight);
            }
        }
        /// <summary>新地图敌人与玩家读取相同边界；旧海岛保留未上岸敌人的海面留量。</summary>
        private static void ResolveBattleEnemyPosition(OnlineArcadeBattleRuntime b,ref float x,ref float z,float previousX,float previousZ,float radius,
            ArcadeRuleConfig rule,IReadOnlyList<ArcadeObstacleConfig> obstacles,ArcadeIslandRegion island,float seaDistance)
        {
            if(EarlyStage(b)!=null)ResolveBattlePosition(b,ref x,ref z,previousX,previousZ,radius,obstacles,island);
            else ResolveEnemyPosition(ref x,ref z,previousX,previousZ,radius,rule,obstacles,island,seaDistance);
        }
    }
}

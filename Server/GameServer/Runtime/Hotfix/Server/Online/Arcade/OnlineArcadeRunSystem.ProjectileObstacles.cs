using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>弹丸只读取实体阻挡；水面等禁行区仍由原列表限制人物移动。</summary>
        private static IReadOnlyList<ArcadeObstacleConfig> ProjectileObstaclesFor(OnlineArcadeBattleRuntime battle)
        {
            var source = EffectiveObstacles(battle);
            if (!ReferenceEquals(source, battle.ProjectileObstacleSource) ||
                !ReferenceEquals(battle.RoundLayout, battle.ProjectileObstacleLayout))
            {
                battle.ProjectileObstacleSource = source;
                battle.ProjectileObstacleLayout = battle.RoundLayout;
                battle.ProjectileObstacles = battle.RoundLayout == null ? source :
                    source.Where((o, i) => battle.RoundLayout.Obstacles[i].BlocksProjectiles).ToArray();
            }
            return battle.ProjectileObstacles;
        }
    }
}

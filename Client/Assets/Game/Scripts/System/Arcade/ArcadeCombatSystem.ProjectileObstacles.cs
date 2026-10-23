using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>缓存当前阻挡快照的实体子集；人物行走继续使用完整阻挡。</summary>
        private IReadOnlyList<ArcadeObstacleDefinition> projectileObstacleSource, projectileObstacles;

        /// <summary>所有弹种共用实体阻挡；只在地图阻挡快照变化时重新筛选。</summary>
        private IReadOnlyList<ArcadeObstacleDefinition> ProjectileObstaclesFor(ArcadeCombatRuntime runtime)
        {
            var source = ObstaclesFor(runtime);
            if (!ReferenceEquals(source, projectileObstacleSource))
            {
                projectileObstacleSource = source;
                projectileObstacles = source.Where(o => o.BlocksProjectiles).ToArray();
            }
            return projectileObstacles;
        }
    }
}

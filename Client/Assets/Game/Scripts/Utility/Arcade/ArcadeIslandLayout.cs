using System;
using UnityEngine;

namespace Game
{
    /// <summary>海岛四轮固定关卡摆放；尺寸相对原场地缩放，表现和规则只读取这一份布局。</summary>
    public static class ArcadeIslandLayout
    {
        /// <summary>固定阻挡范围；第三图第一项是沉船船壳，保留出生区及外围绕行通路。</summary>
        public static ArcadeObstacleDefinition[] GetObstacles(int roundNumber, float halfWidth, float halfHeight)
        {
            Validate(roundNumber);
            float sx = halfWidth / 19.5f, sy = halfHeight / 19.5f;
            ArcadeObstacleDefinition Obstacle(float x, float y, float w, float h) => new(x * sx, y * sy, w * sx, h * sy);
            return roundNumber switch
            {
                1 => new[] { Obstacle(-11, 6, 1.4f, 1), Obstacle(12, -4, 1, 1.4f) },
                2 => new[] { Obstacle(0, 7, 2.8f, 2.2f), Obstacle(0, -6, 2.8f, 2.2f) },
                3 => new[] { Obstacle(4, 3, 2, 5), Obstacle(-9, -6, 1.8f, 1) },
                _ => new[] { Obstacle(-10, 7, 1.3f, 1.6f), Obstacle(11, -8, 1.3f, 1.6f) }
            };
        }

        /// <summary>每图从安全陆地落脚；不恢复旧地图的坐标或消耗冲刺。</summary>
        public static Vector2 GetSpawn(int roundNumber, float halfWidth, float halfHeight)
        {
            Validate(roundNumber);
            // 第二图南侧礁石向两侧膨胀角色半径后覆盖旧起点，改从左侧陆路落脚。
            return new Vector2((roundNumber == 2 ? -5 : -3) * halfWidth / 19.5f, -8 * halfHeight / 19.5f);
        }

        /// <summary>出口落在同源沙地内侧，触发范围及角色半径均留余量；规则与表现共用坐标。</summary>
        public static Vector2 GetExitPosition(int roundNumber, int index, int count, float halfWidth,
            float halfHeight, float playerRadius)
        {
            Validate(roundNumber);
            if (count < 1 || index < 0 || index >= count || playerRadius <= 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            var region = new FirepowerLoop.Arcade.ArcadeIslandRegion(halfWidth, halfHeight, roundNumber);
            var point = region.LandPoint((index - (count - 1) * .5f) * 5f, halfHeight - 3f, playerRadius + 1.5f);
            point = region.Constrain(point.X, point.Y, playerRadius + 1.5f);
            return new Vector2(point.X, point.Y);
        }

        /// <summary>工作名本地化键；文案源由正式配置登记。</summary>
        public static string GetNameKey(int roundNumber)
        {
            Validate(roundNumber);
            return "ui.arcade.island.round." + roundNumber;
        }

        /// <summary>旧地图地标投影至新轮干陆地；规则发现范围与可见入口使用同一落点。</summary>
        public static Vector2 GetLandmarkPosition(FirepowerLoop.Arcade.ArcadeIslandRegion region, Vector2 desired, float inset)
        {
            var land = region.LandPoint(desired.x, desired.y, inset);
            var position = region.Constrain(land.X, land.Y, inset);
            return new Vector2(position.X, position.Y);
        }

        private static void Validate(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > 4) throw new ArgumentOutOfRangeException(nameof(roundNumber));
        }
    }
}

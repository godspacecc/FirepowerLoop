using UnityEngine;

namespace Game
{
    /// <summary>
    /// 已确认卫星底图到和家村全图 XZ 的规划级坐标框架；只负责坐标换算，不推断或生成地物。
    /// </summary>
    public static class HeJiaVillageAnnotationFrame
    {
        public const int ImageWidth = 558;
        public const int ImageHeight = 699;
        public const float TileWorldSizeMeters = 1024f;

        public static readonly Vector2 NorthWest = new Vector2(805.817f, 1725.722f);
        public static readonly Vector2 NorthEast = new Vector2(1397.749f, 1732.609f);
        public static readonly Vector2 SouthWest = new Vector2(812.924f, 989.646f);
        public static readonly Vector2 SouthEast = new Vector2(1404.891f, 996.532f);

        /// <summary>底图坐标左上为零；返回全图 Unity XZ，单位为米。</summary>
        public static Vector2 ImageToWorld(Vector2 imagePoint)
        {
            var u = imagePoint.x / (ImageWidth - 1f);
            var v = imagePoint.y / (ImageHeight - 1f);
            return Vector2.LerpUnclamped(Vector2.LerpUnclamped(NorthWest, NorthEast, u),
                Vector2.LerpUnclamped(SouthWest, SouthEast, u), v);
        }

        /// <summary>反算全图 XZ 所在的底图像素；超出已确认截图范围时返回 false。</summary>
        public static bool TryWorldToImage(Vector2 world, out Vector2 imagePoint)
        {
            var east = NorthEast - NorthWest;
            var south = SouthWest - NorthWest;
            var twist = NorthWest - NorthEast - SouthWest + SouthEast;
            var relative = world - NorthWest;
            var determinant = Cross(east, south);
            if (Mathf.Abs(determinant) < 0.0001f)
            {
                imagePoint = default;
                return false;
            }
            var u = Cross(relative, south) / determinant;
            var v = Cross(east, relative) / determinant;
            for (var iteration = 0; iteration < 5; iteration++)
            {
                var error = east * u + south * v + twist * (u * v) - relative;
                var du = east + twist * v;
                var dv = south + twist * u;
                determinant = Cross(du, dv);
                if (Mathf.Abs(determinant) < 0.0001f) break;
                u -= Cross(error, dv) / determinant;
                v -= Cross(du, error) / determinant;
            }
            imagePoint = new Vector2(u * (ImageWidth - 1f), v * (ImageHeight - 1f));
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }
    }
}

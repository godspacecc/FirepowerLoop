using UnityEngine;

namespace Game
{
    public static class MapDisplayProjection
    {
        private static readonly Rect FullViewport = new Rect(0f, 0f, 1f, 1f);

        public static bool TryWorldToNormalized(MapDisplayDefinition definition, Vector3 worldPosition, out Vector2 normalized)
        {
            normalized = default;
            if (!IsFinite(definition.CenterX) || !IsFinite(definition.CenterZ) ||
                !IsPositiveFinite(definition.Width) || !IsPositiveFinite(definition.Height) ||
                !IsFinite(worldPosition.x) || !IsFinite(worldPosition.z) ||
                worldPosition.x < definition.MinX || worldPosition.x > definition.MaxX ||
                worldPosition.z < definition.MinZ || worldPosition.z > definition.MaxZ) return false;

            normalized = new Vector2(
                (worldPosition.x - definition.MinX) / definition.Width,
                (worldPosition.z - definition.MinZ) / definition.Height);
            return true;
        }

        /// <summary>
        /// 根据玩家位置选择配置网格中的当前分区，并返回玩家在该分区内的局部坐标。
        /// 位于内部边界的点归入右侧或上侧分区，地图最大边界仍归入最后一个分区。
        /// </summary>
        public static bool TryResolveViewport(MapDisplayDefinition definition, Vector3 focusWorldPosition,
            out Rect viewport, out Vector2 localNormalized)
        {
            viewport = FullViewport;
            localNormalized = default;
            if (definition.ViewportColumns <= 0 || definition.ViewportRows <= 0 ||
                !TryWorldToNormalized(definition, focusWorldPosition, out var normalized)) return false;

            var column = Mathf.Min(Mathf.FloorToInt(normalized.x * definition.ViewportColumns),
                definition.ViewportColumns - 1);
            var row = Mathf.Min(Mathf.FloorToInt(normalized.y * definition.ViewportRows),
                definition.ViewportRows - 1);
            viewport = new Rect(
                (float)column / definition.ViewportColumns,
                (float)row / definition.ViewportRows,
                1f / definition.ViewportColumns,
                1f / definition.ViewportRows);
            localNormalized = ToViewportNormalized(normalized, viewport);
            return true;
        }

        /// <summary>把世界坐标换算为指定地图分区内的局部坐标；分区外标记返回 false。</summary>
        public static bool TryWorldToViewportNormalized(MapDisplayDefinition definition, Vector3 worldPosition,
            Rect viewport, out Vector2 localNormalized)
        {
            localNormalized = default;
            if (viewport.width <= 0f || viewport.height <= 0f ||
                !TryWorldToNormalized(definition, worldPosition, out var normalized)) return false;

            var isLastColumn = Mathf.Approximately(viewport.xMax, 1f);
            var isLastRow = Mathf.Approximately(viewport.yMax, 1f);
            if (normalized.x < viewport.xMin || normalized.y < viewport.yMin ||
                (isLastColumn ? normalized.x > viewport.xMax : normalized.x >= viewport.xMax) ||
                (isLastRow ? normalized.y > viewport.yMax : normalized.y >= viewport.yMax)) return false;

            localNormalized = ToViewportNormalized(normalized, viewport);
            return true;
        }

        private static Vector2 ToViewportNormalized(Vector2 normalized, Rect viewport) => new Vector2(
            (normalized.x - viewport.xMin) / viewport.width,
            (normalized.y - viewport.yMin) / viewport.height);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsPositiveFinite(float value) => IsFinite(value) && value > 0f;
    }
}

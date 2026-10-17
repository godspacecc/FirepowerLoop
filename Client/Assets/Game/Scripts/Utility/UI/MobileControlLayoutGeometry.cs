using UnityEngine;

namespace Game
{
    /// <summary>将设备布局适配到实际安全区；避免初始触摸面覆盖固定按钮，移动行程保留在屏幕内。</summary>
    public static class MobileControlLayoutGeometry
    {
        /// <summary>编辑页与实战按相同动作顺序适配按钮；隐藏的技能和捐命保留位置，第一人称入口只在对应模式避让。</summary>
        public static Rect[] RenderArcadeButtons(RectTransform area, MobileControlLayoutData data, RectTransform[] buttons, bool firstPerson)
        {
            var occupied = new System.Collections.Generic.List<Rect>();
            for (var i = 0; i < buttons.Length; i++)
            {
                RenderButton(area, buttons[i], data.Button((MobileLayoutButton)i), occupied.ToArray());
                if (i < (int)MobileLayoutButton.ExitFirstPerson || firstPerson) occupied.Add(Bounds(area, buttons[i]));
            }
            return occupied.ToArray();
        }

        /// <summary>独立信息栏按保存的中心与缩放适配屏幕边界；保留 Prefab 锚点，不修改设置值。</summary>
        public static void RenderHud(RectTransform area, RectTransform target, MobileHudLayoutData data)
        {
            target.localScale = Vector3.one * data.Scale;
            var center = FitButton(area.rect.size, new MobileButtonLayoutData { X = data.X, Y = data.Y },
                target.rect.size * data.Scale);
            target.position = area.TransformPoint(area.rect.min + center) - target.TransformVector(target.rect.center);
        }

        /// <summary>在安全区内寻找不覆盖已有控件的最近按钮中心；适配只影响显示，不覆写保存位置。</summary>
        public static Vector2 FitButton(Vector2 size, MobileButtonLayoutData data, Vector2 footprint, params Rect[] obstacles)
        {
            var half = footprint * .5f + Vector2.one * 8f;
            var min = Vector2.Min(half, size * .5f);
            var max = Vector2.Max(min, size - half);
            var desired = new Vector2(Mathf.Clamp(data.X * size.x, min.x, max.x), Mathf.Clamp(data.Y * size.y, min.y, max.y));
            bool Free(Vector2 point)
            {
                var rect = new Rect(point - half, half * 2f);
                foreach (var obstacle in obstacles) if (obstacle.width > 0 && obstacle.height > 0 && rect.Overlaps(obstacle)) return false;
                return true;
            }
            if (Free(desired)) return desired;
            var best = desired;
            var distance = float.PositiveInfinity;
            for (var x = 0; x <= 40; x++)
            for (var y = 0; y <= 30; y++)
            {
                var candidate = new Vector2(Mathf.Lerp(min.x, max.x, x / 40f), Mathf.Lerp(min.y, max.y, y / 30f));
                var d = (candidate - desired).sqrMagnitude;
                if (d < distance && Free(candidate)) { best = candidate; distance = d; }
            }
            return best;
        }

        /// <summary>按钮尺寸来自 Prefab；按实际父容器转换安全区坐标，支持分属不同 HUD 的入口。</summary>
        public static void RenderButton(RectTransform area, RectTransform button, MobileButtonLayoutData data, params Rect[] obstacles)
        {
            var center = FitButton(area.rect.size, data, button.rect.size, obstacles);
            button.anchorMin = button.anchorMax = button.pivot = Vector2.one * .5f;
            button.position = area.TransformPoint(area.rect.min + center);
        }

        public const float StickSize = 180f;
        public const float Travel = 75f;

        /// <summary>按安全区 Canvas 单位限制中心；触摸面避让固定按钮，左右摇杆留在各自半区。</summary>
        /// <returns>安全区左下为原点的中心位置；只产生显示适配值，不修改传入设置。</returns>
        public static Vector2 Fit(Vector2 size, MobileStickLayoutData data, bool move, params Rect[] obstacles)
        {
            var radius = (StickSize * 0.5f + Travel) * data.Scale;
            var min = new Vector2(radius, radius);
            var max = new Vector2(size.x - radius, Mathf.Min(size.y - radius, size.y * 0.45f));
            if (move) max.x = Mathf.Min(max.x, size.x * 0.48f - StickSize * data.Scale * 0.5f);
            else min.x = Mathf.Max(min.x, size.x * 0.52f + StickSize * data.Scale * 0.5f);
            max = Vector2.Max(min, max);
            var desired = new Vector2(Mathf.Clamp(data.X * size.x, min.x, max.x),
                Mathf.Clamp(data.Y * size.y, min.y, max.y));
            if (IsFree(desired, data.Scale, obstacles)) return desired;

            // 分辨率变化可能让原布局碰到固定按钮；只适配显示，不覆写玩家保存值。
            var best = desired;
            var distance = float.PositiveInfinity;
            for (var x = 0; x <= 20; x++)
            for (var y = 0; y <= 20; y++)
            {
                var candidate = new Vector2(Mathf.Lerp(min.x, max.x, x / 20f), Mathf.Lerp(min.y, max.y, y / 20f));
                var d = (candidate - desired).sqrMagnitude;
                if (d < distance && IsFree(candidate, data.Scale, obstacles)) { best = candidate; distance = d; }
            }
            return best;
        }

        public static bool IsFree(Vector2 center, float scale, params Rect[] obstacles)
        {
            var diameter = StickSize * scale + 16f;
            var touch = new Rect(center - Vector2.one * diameter * 0.5f, Vector2.one * diameter);
            foreach (var obstacle in obstacles) if (touch.Overlaps(obstacle)) return false;
            return true;
        }

        /// <summary>读取任意子控件在安全区中的边界，坐标原点为安全区左下。</summary>
        public static Rect Bounds(RectTransform area, RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var a = (Vector2)area.InverseTransformPoint(corners[0]) - area.rect.min;
            var b = (Vector2)area.InverseTransformPoint(corners[2]) - area.rect.min;
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        public static void Render(RectTransform area, RectTransform wrapper, CanvasGroup opacity,
            MobileStickLayoutData data, bool move, params Rect[] obstacles)
        {
            var center = Fit(area.rect.size, data, move, obstacles);
            wrapper.anchorMin = wrapper.anchorMax = Vector2.zero;
            wrapper.pivot = Vector2.one * 0.5f;
            wrapper.anchoredPosition = center;
            wrapper.localScale = Vector3.one * data.Scale;
            opacity.alpha = data.Opacity;
        }
    }
}

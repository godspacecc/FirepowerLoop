using System;
using System.Collections.Generic;

namespace AnotherDay.Arcade
{
    /// <summary>竞技场平面米；双端规则及网格共享，不含 Unity 对象。</summary>
    public readonly struct ArcadeRegionPoint
    {
        public ArcadeRegionPoint(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
    }

    /// <summary>海岛唯一轮廓定义：外缘限制行走，内缘是沙滩，二者之间为浅水；不决定敌群、伤害和存档。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeIslandRegion
    {
        public const int Samples = 96;
        private readonly ArcadeRegionPoint[] outer = new ArcadeRegionPoint[Samples];
        private readonly ArcadeRegionPoint[] sand = new ArcadeRegionPoint[Samples];
        public IReadOnlyList<ArcadeRegionPoint> Outer => outer;
        public IReadOnlyList<ArcadeRegionPoint> Sand => sand;

        /// <summary>保留原场地尺度，浅凹岸线及宽主战斗区；制作工具与双方权威使用同一采样结果。</summary>
        public ArcadeIslandRegion(float halfWidth, float halfHeight)
        {
            if (halfWidth <= 5 || halfHeight <= 5 || float.IsNaN(halfWidth) || float.IsNaN(halfHeight) ||
                float.IsInfinity(halfWidth) || float.IsInfinity(halfHeight)) throw new ArgumentException("海岛尺度无效");
            for (int i = 0; i < Samples; i++)
            {
                double a = i * Math.PI * 2 / Samples, c = Math.Cos(a), s = Math.Sin(a);
                double outline = Math.Pow(Math.Pow(Math.Abs(c), 4) + Math.Pow(Math.Abs(s), 4), -.25);
                double variation = (.975 + .018 * Math.Sin(3 * a + .6) + .012 * Math.Cos(5 * a)) / 1.005;
                float x = (float)(c * outline * variation * halfWidth);
                float y = (float)(s * outline * variation * halfHeight);
                outer[i] = new ArcadeRegionPoint(x, y);
                float radius = (float)Math.Sqrt(x * x + y * y);
                // 北岸窄水带，南侧及两侧宽浅水；宽度随岸段连续变化，便于区分两类来敌。
                float width = .5f + (float)(2.5 * Math.Pow(1 - Math.Max(0, s), 2)) + (float)(.25 * Math.Sin(2 * a));
                float scale = (radius - width) / radius;
                sand[i] = new ArcadeRegionPoint(x * scale, y * scale);
            }
        }

        public bool Contains(float x, float y) => Inside(outer, x, y);
        public bool IsShallow(float x, float y) => Contains(x, y) && !Inside(sand, x, y);
        public ArcadeRegionPoint LandPoint(float x, float y, float inset)
        {
            float length = (float)Math.Sqrt(x * x + y * y);
            if (length < .001f) return new ArcadeRegionPoint(0, 0);
            int i = ((int)Math.Round(Math.Atan2(y, x) / (Math.PI * 2) * Samples) + Samples) % Samples;
            var edge = sand[i]; float limit = (float)Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y) - inset;
            float scale = Math.Min(1, Math.Max(0, limit) / length);
            return new ArcadeRegionPoint(x * scale, y * scale);
        }

        private static bool Inside(ArcadeRegionPoint[] polygon, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i]; var b = polygon[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>圆形对象投影至内侧；通常只作局部修正，非法恢复/生成坐标也可回到最近可达位置。</summary>
        public ArcadeRegionPoint Constrain(float x, float y, float radius)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                float closest = float.MaxValue, px = 0, py = 0, nx = 0, ny = 0;
                for (int i = 0; i < Samples; i++)
                {
                    var a = outer[i]; var b = outer[(i + 1) % Samples];
                    float dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                    float t = Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / len2));
                    float qx = a.X + dx * t, qy = a.Y + dy * t;
                    float distance2 = (x - qx) * (x - qx) + (y - qy) * (y - qy);
                    if (distance2 >= closest) continue;
                    closest = distance2; px = qx; py = qy;
                    float length = (float)Math.Sqrt(len2); nx = -dy / length; ny = dx / length;
                }
                if (Inside(outer, x, y) && closest >= radius * radius - .00001f) return new ArcadeRegionPoint(x, y);
                x = px + nx * (radius + .002f); y = py + ny * (radius + .002f);
            }
            return new ArcadeRegionPoint(x, y);
        }

        /// <summary>短步沿外缘滑动，记录最终真实路径供高速接触判定；不会用越界目标虚构冲刺轨迹。</summary>
        public ArcadeRegionPoint Move(float x, float y, float dx, float dy, float radius,
            List<ArcadeRegionPoint> path = null)
        {
            path?.Clear(); var p = Constrain(x, y, radius); path?.Add(p);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / .25));
            for (int i = 0; i < steps; i++)
            {
                p = Constrain(p.X + dx / steps, p.Y + dy / steps, radius); path?.Add(p);
            }
            return p;
        }

        /// <summary>在当前轮廓内的北岸/南水侧交替生成；外缘留角色半径，水侧终点进入沙滩。</summary>
        public void Entry(int sequence, float lane, float radius, out ArcadeRegionPoint start, out ArcadeRegionPoint target)
        {
            bool water = (sequence & 1) == 0;
            double angle = (water ? Math.PI * 1.5 : Math.PI * .5) + (Math.Max(0, Math.Min(1, lane)) - .5) * 1.2;
            int i = ((int)Math.Round(angle / (Math.PI * 2) * Samples) + Samples) % Samples;
            var o = outer[i]; var s = sand[i];
            float len = (float)Math.Sqrt(o.X * o.X + o.Y * o.Y);
            start = Constrain(o.X * (1 - (radius + .1f) / len), o.Y * (1 - (radius + .1f) / len), radius);
            float slen = (float)Math.Sqrt(s.X * s.X + s.Y * s.Y);
            target = Constrain(s.X * (1 - radius * 2 / slen), s.Y * (1 - radius * 2 / slen), radius);
        }
    }
}

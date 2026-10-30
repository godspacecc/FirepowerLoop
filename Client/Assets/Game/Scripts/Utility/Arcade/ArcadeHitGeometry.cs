using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>无 Unity 依赖的三维命中坐标，Y 为高度。</summary>
    public readonly struct ArcadeHitPoint
    {
        /// <summary>三个空间坐标分量。</summary>
        public readonly double X, Y, Z;
        /// <summary>构造坐标；有效性由查询入口统一检查。</summary>
        public ArcadeHitPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>两端球心和半径定义的身体代理胶囊。</summary>
    public readonly struct ArcadeHitCapsule
    {
        /// <summary>中心轴的两个端点。</summary>
        public readonly ArcadeHitPoint A, B;
        /// <summary>胶囊半径，必须有限且非负。</summary>
        public readonly double Radius;
        /// <summary>构造胶囊；重合端点自然退化为球。</summary>
        public ArcadeHitCapsule(ArcadeHitPoint a, ArcadeHitPoint b, double radius) { A = a; B = b; Radius = radius; }
    }

    /// <summary>纯数学三维查询；不读取动画，不以入场时间代替碰撞。</summary>
    public static class ArcadeHitGeometry
    {
        /// <summary>球与胶囊接触也算重叠；非法输入返回 false。</summary>
        public static bool OverlapsSphere(ArcadeHitPoint center, double radius, ArcadeHitCapsule capsule)
        {
            if (!Valid(center) || !Valid(capsule.A) || !Valid(capsule.B) || !RadiusValid(radius) || !RadiusValid(capsule.Radius)) return false;
            var axis = Sub(capsule.B, capsule.A);
            double length = Length(axis), combined = radius + capsule.Radius;
            if (!Finite(length) || !Finite(combined)) return false;
            var offset = Sub(center, capsule.A);
            if (length > 0) { var unit = Scale(axis, 1 / length); offset = Sub(offset, Scale(unit, Math.Max(0, Math.Min(length, Dot(offset, unit))))); }
            return Length(offset) <= combined;
        }

        /// <summary>球沿线段扫掠胶囊，返回最早接触比例；起点重叠为零。非法或数值溢出输入返回 false。</summary>
        public static bool TrySweepSphere(ArcadeHitPoint from, ArcadeHitPoint to, double radius, ArcadeHitCapsule capsule, out double fraction)
        {
            fraction = 0;
            if (!Valid(from) || !Valid(to) || !Valid(capsule.A) || !Valid(capsule.B) || !RadiusValid(radius) || !RadiusValid(capsule.Radius)) return false;
            double combined = radius + capsule.Radius;
            if (!Finite(combined)) return false;
            if (OverlapsSphere(from, radius, capsule)) return true;
            var delta = Sub(to, from);
            double travel = Length(delta);
            if (!(travel > 0) || !Finite(travel)) return false;
            var direction = Scale(delta, 1 / travel);
            var axis = Sub(capsule.B, capsule.A);
            double height = Length(axis), first = double.PositiveInfinity;
            if (!Finite(height)) return false;
            // 胶囊是有限圆柱与两个端球的并集，分别求最早进入点。
            SphereEntry(from, direction, capsule.A, combined, travel, ref first);
            SphereEntry(from, direction, capsule.B, combined, travel, ref first);
            if (height > 0)
            {
                var unit = Scale(axis, 1 / height);
                var offset = Sub(from, capsule.A);
                double along = Dot(offset, unit), slope = Dot(direction, unit);
                var radial = Sub(offset, Scale(unit, along));
                var radialVelocity = Sub(direction, Scale(unit, slope));
                double speed = Length(radialVelocity);
                if (speed > 0)
                {
                    var radialDirection = Scale(radialVelocity, 1 / speed);
                    double closest = -Dot(radial, radialDirection);
                    double distance = Length(Add(radial, Scale(radialDirection, closest)));
                    if (distance <= combined)
                    {
                        double half = Math.Sqrt(Math.Max(0, (combined - distance) * (combined + distance)));
                        double entry = (closest - half) / speed;
                        double axial = along + entry * slope;
                        if (entry >= 0 && entry <= travel && axial >= 0 && axial <= height) first = Math.Min(first, entry);
                    }
                }
            }
            if (!Finite(first)) return false;
            fraction = Math.Max(0, Math.Min(1, first / travel));
            return true;
        }

        /// <summary>用最近点形式避免长线段二次方程判别式的相减损失。</summary>
        private static void SphereEntry(ArcadeHitPoint from, ArcadeHitPoint direction, ArcadeHitPoint center, double radius, double travel, ref double first)
        {
            var offset = Sub(from, center);
            double closest = -Dot(offset, direction);
            double distance = Length(Add(offset, Scale(direction, closest)));
            if (distance > radius) return;
            double entry = closest - Math.Sqrt(Math.Max(0, (radius - distance) * (radius + distance)));
            if (entry >= 0 && entry <= travel) first = Math.Min(first, entry);
        }
        /// <summary>检查有限实数。</summary>
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        /// <summary>检查半径。</summary>
        private static bool RadiusValid(double v) => Finite(v) && v >= 0;
        /// <summary>检查坐标。</summary>
        private static bool Valid(ArcadeHitPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        /// <summary>向量相减。</summary>
        private static ArcadeHitPoint Sub(ArcadeHitPoint a, ArcadeHitPoint b) => new ArcadeHitPoint(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        /// <summary>向量相加。</summary>
        private static ArcadeHitPoint Add(ArcadeHitPoint a, ArcadeHitPoint b) => new ArcadeHitPoint(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        /// <summary>向量缩放。</summary>
        private static ArcadeHitPoint Scale(ArcadeHitPoint a, double k) => new ArcadeHitPoint(a.X*k,a.Y*k,a.Z*k);
        /// <summary>向量内积。</summary>
        private static double Dot(ArcadeHitPoint a, ArcadeHitPoint b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        /// <summary>向量长度。</summary>
        private static double Length(ArcadeHitPoint a) => Math.Sqrt(Dot(a,a));
    }
}

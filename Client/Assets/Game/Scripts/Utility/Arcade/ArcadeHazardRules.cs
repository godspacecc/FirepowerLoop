using System;
using System.Collections.Generic;

namespace FirepowerLoop.Arcade
{
    /// <summary>双端共用的不可变机关参数；坐标为场地米，时间为秒，伤害为最大生命比例，不持有 Unity 对象。</summary>
    public readonly struct ArcadeHazardParameters
    {
        public ArcadeHazardParameters(int id, int kind, float x, float y, float directionDegrees,
            float radius, float armLength, float armHalfWidth, float idleSeconds, float warningSeconds,
            float activeSeconds, float angularSpeedDegrees, float hitIntervalSeconds,
            float playerDamageRatio, float bossDamageRatio)
        {
            Id = id; Kind = kind; X = x; Y = y; DirectionDegrees = directionDegrees;
            Radius = radius; ArmLength = armLength; ArmHalfWidth = armHalfWidth;
            IdleSeconds = idleSeconds; WarningSeconds = warningSeconds; ActiveSeconds = activeSeconds;
            AngularSpeedDegrees = angularSpeedDegrees; HitIntervalSeconds = hitIntervalSeconds;
            PlayerDamageRatio = playerDamageRatio; BossDamageRatio = bossDamageRatio;
        }
        /// <summary>本场配置内稳定且唯一的正整数，不跨场复用运行态。</summary>
        public int Id { get; }
        /// <summary>1 通电塔，2 旋转横臂，与 Luban 枚举一致。</summary>
        public int Kind { get; }
        public float X { get; }
        public float Y { get; }
        public float DirectionDegrees { get; }
        public float Radius { get; }
        public float ArmLength { get; }
        public float ArmHalfWidth { get; }
        public float IdleSeconds { get; }
        public float WarningSeconds { get; }
        public float ActiveSeconds { get; }
        public float AngularSpeedDegrees { get; }
        public float HitIntervalSeconds { get; }
        /// <summary>有效命中扣除玩家最大生命的比例；1 表示满血也失命，仍由受伤入口先裁决护盾与无敌。</summary>
        public float PlayerDamageRatio { get; }
        public float BossDamageRatio { get; }
        public float CycleSeconds => IdleSeconds + WarningSeconds + ActiveSeconds;
        public float Extent => Kind == 1 ? Radius : ArmLength + ArmHalfWidth;
    }

    /// <summary>单台机关的瞬时权威状态；目标冷却与旋转去重仅在本场存在，不参与安全存档或客户端裁决。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeHazardState
    {
        private readonly Dictionary<long, double> nextHits = new Dictionary<long, double>();
        private readonly Dictionary<long, long> hitSweeps = new Dictionary<long, long>();
        public ArcadeHazardState(ArcadeHazardParameters parameters) { Parameters = parameters; }
        public ArcadeHazardParameters Parameters { get; }
        public double Elapsed { get; private set; }
        public double PreviousElapsed { get; private set; }

        /// <summary>仅由活动战斗帧调用；暂停与复活等待不调用，恢复不会补算暂停时间。</summary>
        public void Advance(float seconds)
        {
            PreviousElapsed = Elapsed;
            if (seconds > 0f) Elapsed += seconds;
        }

        /// <summary>目标身份为正数敌人、负数玩家；免伤目标由调用者提前跳过，护盾有效挡伤仍消耗接触间隔。</summary>
        public bool TryHit(long targetId, float previousX, float previousY, float x, float y, float radius)
        {
            if (nextHits.TryGetValue(targetId, out double next) && Elapsed < next) return false;
            double from = Math.Max(PreviousElapsed, next);
            if (Parameters.Kind == 2 && hitSweeps.TryGetValue(targetId, out long previousSweep))
            {
                double cycleStart = (previousSweep >> 32) * (double)Parameters.CycleSeconds;
                double sweepEnd = cycleStart + Parameters.IdleSeconds + Parameters.WarningSeconds +
                    ((uint)previousSweep + 1d) * 360d / Math.Abs(Parameters.AngularSpeedDegrees);
                // 跳过已结算扫过，仍检查同一长帧里的后续整圈或新周期。
                from = Math.Max(from, Math.Min(sweepEnd, cycleStart + Parameters.CycleSeconds));
            }
            // 冷却或扫过去重裁剪时间时，同步裁剪目标路径，不能把旧位置搬到新时刻。
            if (Elapsed > PreviousElapsed && from > PreviousElapsed)
            {
                float fraction = (float)Math.Min(1d, (from - PreviousElapsed) / (Elapsed - PreviousElapsed));
                previousX += (x - previousX) * fraction;
                previousY += (y - previousY) * fraction;
            }
            if (!ArcadeHazardRules.Intersects(Parameters, from, Elapsed,
                    previousX, previousY, x, y, radius, out long sweep)) return false;
            if (Parameters.Kind == 2 && hitSweeps.TryGetValue(targetId, out long oldSweep) && oldSweep == sweep)
                return false;
            nextHits[targetId] = Elapsed + Parameters.HitIntervalSeconds;
            if (Parameters.Kind == 2) hitSweeps[targetId] = sweep;
            return true;
        }

        /// <summary>敌人移除时释放身份记录，避免长期场次或对象池身份复用继承旧冷却。</summary>
        public void ForgetTarget(long targetId) { nextHits.Remove(targetId); hitSweeps.Remove(targetId); }
    }

    /// <summary>纯几何、周期与配置校验；双端链接同一源码，表现端只读取 Evaluate，不持有伤害权威。</summary>
    public static class ArcadeHazardRules
    {
        /// <summary>输出 0 安全、1 预警、2 活动，角度以竞技场 X 轴为零方向。</summary>
        public static void Evaluate(ArcadeHazardParameters p, double elapsed, out int phase, out float angle)
        {
            double local = Math.Max(0, elapsed) % p.CycleSeconds;
            double start = p.IdleSeconds + p.WarningSeconds;
            phase = local < p.IdleSeconds ? 0 : local < start ? 1 : 2;
            angle = p.DirectionDegrees + (phase == 2 ? (float)(local - start) * p.AngularSpeedDegrees : 0f);
        }

        /// <summary>检测本帧活动片段；线性运动与旋转采用有误差上界的细分，防止长帧/冲刺穿过细臂漏判。</summary>
        public static bool Intersects(ArcadeHazardParameters p, double fromTime, double toTime,
            float fromX, float fromY, float toX, float toY, float radius, out long sweep)
        {
            sweep = -1;
            if (toTime <= fromTime) return false;
            double cycle = p.CycleSeconds;
            long first = (long)Math.Floor(fromTime / cycle);
            long last = (long)Math.Floor(toTime / cycle);
            for (long index = first; index <= last; index++)
            {
                double activeStart = index * cycle + p.IdleSeconds + p.WarningSeconds;
                double start = Math.Max(fromTime, activeStart);
                double end = Math.Min(toTime, (index + 1) * cycle);
                if (end < start || end <= activeStart) continue;
                float a = (float)((start - fromTime) / (toTime - fromTime));
                float b = (float)((end - fromTime) / (toTime - fromTime));
                float sx = fromX + (toX - fromX) * a, sy = fromY + (toY - fromY) * a;
                float ex = fromX + (toX - fromX) * b, ey = fromY + (toY - fromY) * b;
                if (p.Kind == 1)
                {
                    if (SegmentDistanceSquared(p.X, p.Y, sx, sy, ex, ey) <= Square(p.Radius + radius))
                        return true;
                    continue;
                }
                double radians = Math.Abs(p.AngularSpeedDegrees) * (end - start) * Math.PI / 180;
                double movement = Math.Sqrt(Square(ex - sx) + Square(ey - sy));
                int steps = Math.Max(1, (int)Math.Ceiling((movement + p.ArmLength * radians) / 0.05));
                // 每步中目标与臂的最大位移一半作为膨胀，保守误差不超过约 2.5 厘米。
                float padding = (float)((movement + p.ArmLength * radians) / steps * 0.5);
                for (int step = 0; step <= steps; step++)
                {
                    double t = start + (end - start) * step / steps;
                    double angle = (p.DirectionDegrees + (t - activeStart) * p.AngularSpeedDegrees) * Math.PI / 180;
                    float x = sx + (ex - sx) * step / steps, y = sy + (ey - sy) * step / steps;
                    float armX = p.X + (float)Math.Cos(angle) * p.ArmLength;
                    float armY = p.Y + (float)Math.Sin(angle) * p.ArmLength;
                    if (SegmentDistanceSquared(x, y, p.X, p.Y, armX, armY) > Square(radius + p.ArmHalfWidth + padding))
                        continue;
                    // 活动周期和整圈编号共同标识扫过；新周期允许再次命中。
                    sweep = (index << 32) | (uint)Math.Floor(Math.Abs((t - activeStart) * p.AngularSpeedDegrees) / 360);
                    return true;
                }
            }
            return false;
        }

        public static int Damage(int maximumHealth, float ratio) => Math.Max(1, (int)Math.Round(maximumHealth * (double)ratio));

        /// <summary>双端启动时拒绝无效数值、越界和中心/双人出生点危险区；不静默修正配置。</summary>
        public static string Validate(ArcadeHazardParameters p, float halfWidth, float halfHeight, float playerRadius)
        {
            float[] values = { p.X, p.Y, p.DirectionDegrees, p.Radius, p.ArmLength, p.ArmHalfWidth,
                p.IdleSeconds, p.WarningSeconds, p.ActiveSeconds, p.AngularSpeedDegrees, p.HitIntervalSeconds,
                p.PlayerDamageRatio, p.BossDamageRatio };
            foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) return "机关数值必须有限";
            if (p.Id <= 0 || p.Kind < 1 || p.Kind > 2 || p.IdleSeconds <= 0 || p.WarningSeconds <= 0 ||
                p.ActiveSeconds <= 0 || p.HitIntervalSeconds <= 0 || p.PlayerDamageRatio <= 0 || p.PlayerDamageRatio > 1 ||
                p.BossDamageRatio <= 0 || p.BossDamageRatio >= 1 ||
                p.Kind == 1 && (p.Radius <= 0 || p.ArmLength != 0 || p.ArmHalfWidth != 0) ||
                p.Kind == 2 && (p.ArmLength <= 0 || p.ArmHalfWidth <= 0 || p.AngularSpeedDegrees == 0))
                return "机关种类、尺寸、周期或伤害无效";
            if (Math.Abs(p.X) + p.Extent + playerRadius >= halfWidth ||
                Math.Abs(p.Y) + p.Extent + playerRadius >= halfHeight) return "机关危险范围超出场地";
            for (int spawn = -2; spawn <= 2; spawn += 2)
                if (Square(p.X - spawn) + Square(p.Y) <= Square(p.Extent + playerRadius + 0.5f))
                    return "机关覆盖中心复活区或双人出生区";
            return null;
        }

        public static float SegmentDistanceSquared(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float length = dx * dx + dy * dy;
            float t = length > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / length)) : 0;
            return Square(x - ax - dx * t) + Square(y - ay - dy * t);
        }
        private static float Square(float x) => x * x;
    }
}

using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>敌人从场外沿指定直线接近入口的权威状态，不提供免伤或表现资格。</summary>
    public struct ArcadeOutsideEntryState
    {
        /// <summary>是否仍在接近入口。</summary>
        public bool Active;
        /// <summary>场外起点坐标。</summary>
        public float StartX, StartZ;
        /// <summary>场内交接点坐标。</summary>
        public float TargetX, TargetZ;
        /// <summary>已移动距离及线段总长，单位米。</summary>
        public float DistanceMoved, Distance;
    }

    /// <summary>双端纯规则：只沿调用者提供的通道连续移动，不夹回地图边界。</summary>
    public static class ArcadeOutsideEntryRules
    {
        /// <summary>验证有限且非零线段；地图走廊可行性由调用者保证。</summary>
        public static bool TryStart(float startX, float startZ, float targetX, float targetZ, out ArcadeOutsideEntryState state)
        {
            state = default;
            if (!Finite(startX) || !Finite(startZ) || !Finite(targetX) || !Finite(targetZ)) return false;
            double dx = (double)targetX - startX, dz = (double)targetZ - startZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance <= 0 || distance > float.MaxValue || (float)distance <= 0) return false;
            state = new ArcadeOutsideEntryState { Active = true, StartX = startX, StartZ = startZ,
                TargetX = targetX, TargetZ = targetZ, Distance = (float)distance };
            return true;
        }

        /// <summary>返回本帧是否由入场移动占用；抵达帧仍占用，下一帧才交接常规追击。</summary>
        public static bool Advance(ref ArcadeOutsideEntryState state, float seconds, float speed, out ArcadeRegionPoint position)
        {
            position = Position(state);
            if (!state.Active) return false;
            // 无效时间或速度拒绝推进，仍由入场状态持有本帧，避免交给场内逻辑瞬移。
            if (!Finite(seconds) || !Finite(speed) || seconds <= 0 || speed <= 0) return true;
            state.DistanceMoved = (float)Math.Min(state.Distance, state.DistanceMoved + (double)seconds * speed);
            if (state.DistanceMoved >= state.Distance) state.Active = false;
            position = Position(state);
            return true;
        }

        /// <summary>插值使用双精度中间值，终点精确返回，避免大步长越过入口。</summary>
        private static ArcadeRegionPoint Position(ArcadeOutsideEntryState state)
        {
            if (state.Distance <= 0 || state.DistanceMoved >= state.Distance)
                return new ArcadeRegionPoint(state.TargetX, state.TargetZ);
            double t = state.DistanceMoved / (double)state.Distance;
            return new ArcadeRegionPoint((float)(state.StartX + ((double)state.TargetX - state.StartX) * t),
                (float)(state.StartZ + ((double)state.TargetZ - state.StartZ) * t));
        }

        /// <summary>兼容 Unity 与服务器运行时的有限数判断。</summary>
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

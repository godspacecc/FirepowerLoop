using UnityEngine;

namespace Game
{
    /// <summary>只累积真实水平距离并限制发声节奏；不决定移动、地表或角色存活状态。</summary>
    public sealed class FootstepCadence
    {
        private readonly float walkStride, runStride, minimumInterval, fastSpeed;
        // distance 为米，elapsed 为秒；停用、停步和离地清空，长帧不排队补步。
        private float distance, elapsed;
        public bool IsFast { get; private set; }

        public FootstepCadence(float walkStride, float runStride, float minimumInterval, float fastSpeed)
        {
            this.walkStride = Mathf.Max(0.05f, walkStride);
            this.runStride = Mathf.Max(0.05f, runStride);
            this.minimumInterval = Mathf.Max(0f, minimumInterval);
            this.fastSpeed = Mathf.Max(0.1f, fastSpeed);
        }

        /// <summary>每次实际移动后调用；最多返回一个脚步，无效状态丢弃未完成步距。</summary>
        public bool Step(float horizontalDistance, float deltaTime, bool eligible, bool grounded)
        {
            if (!eligible || !grounded || !Finite(horizontalDistance) || !Finite(deltaTime) ||
                horizontalDistance <= 0.0001f || deltaTime <= 0f)
            {
                Reset();
                return false;
            }
            IsFast = horizontalDistance / deltaTime >= fastSpeed;
            distance += horizontalDistance;
            elapsed += deltaTime;
            float stride = IsFast ? runStride : walkStride;
            if (distance < stride) return false;
            // 即使间隔未到也丢弃多余完整步数，避免随后集中补播。
            distance %= stride;
            if (elapsed < minimumInterval) return false;
            elapsed = 0f;
            return true;
        }

        /// <summary>离散定位和表现停用时调用；不保留旧地图或旧生命周期的步距。</summary>
        public void Reset() { distance = elapsed = 0f; IsFast = false; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

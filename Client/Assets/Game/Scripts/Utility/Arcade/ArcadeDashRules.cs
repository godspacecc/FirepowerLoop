using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>单人、服务端与联机预测共用的冲刺方向规则，不持有输入或计时状态。</summary>
    public static class ArcadeDashRules
    {
        /// <summary>有效移动输入优先，否则使用当前朝向；统一为单位向量，缺少朝向时保持默认前方。</summary>
        public static void ResolveDirection(float moveX, float moveY, float facingX, float facingY,
            out float directionX, out float directionY)
        {
            directionX = moveX;
            directionY = moveY;
            if (!TryNormalize(ref directionX, ref directionY))
            {
                directionX = facingX;
                directionY = facingY;
                if (!TryNormalize(ref directionX, ref directionY))
                {
                    directionX = 0;
                    directionY = 1;
                }
            }
        }

        /// <summary>忽略零向量与非法分量，防止旧节点缺少方向时生成无效位移。</summary>
        private static bool TryNormalize(ref float x, ref float y)
        {
            float squared = x * x + y * y;
            if (float.IsNaN(squared) || float.IsInfinity(squared) || squared < .0001f) return false;
            float length = (float)Math.Sqrt(squared);
            x /= length;
            y /= length;
            return true;
        }
    }
}

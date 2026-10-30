using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;

namespace ET
{
    public sealed partial class ServerConfig
    {
        /// <summary>缓存所属配置实例；换表后丢弃旧身体采样。</summary>
        private Tables hitPoseTables;
        /// <summary>正式资源地址对应的已验证不可变身体采样。</summary>
        private readonly Dictionary<string, ArcadeHitPoseSequence> hitPoses = new Dictionary<string, ArcadeHitPoseSequence>(StringComparer.Ordinal);

        /// <summary>只接受正式 Prefab 地址；缺行或损坏时返回具体错误，不猜默认身体。</summary>
        public bool TryGetDockHitPose(string prefabAddress, out ArcadeHitPoseSequence value, out string error)
        {
            value = null; error = null;
            var current = tables;
            if (current == null) { error = "TbArcadeDockHitPose: Tables 尚未加载"; return false; }
            if (!ReferenceEquals(hitPoseTables, current)) { hitPoses.Clear(); hitPoseTables = current; }
            if (string.IsNullOrWhiteSpace(prefabAddress)) { error = "TbArcadeDockHitPose[空地址].Id 无效"; return false; }
            if (hitPoses.TryGetValue(prefabAddress, out value)) return true;
            var row = current.TbArcadeDockHitPose.GetOrDefault(prefabAddress);
            if (row == null) { error = $"TbArcadeDockHitPose[{prefabAddress}].Id 不存在"; return false; }
            try
            {
                value = ArcadeHitPoseSequence.Parse(row.FrameCount, row.CapsuleCount, row.IntervalSeconds, row.Data);
                hitPoses.Add(prefabAddress, value);
                return true;
            }
            catch (ArgumentException exception)
            {
                error = $"TbArcadeDockHitPose[{prefabAddress}]: {exception.Message}";
                value = null; return false;
            }
        }
    }
}

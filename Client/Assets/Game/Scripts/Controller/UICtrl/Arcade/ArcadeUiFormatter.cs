using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>把街机领域快照转换为本地化显示文本；不缓存或修改任何权威状态。</summary>
    internal static class ArcadeUiFormatter
    {
        public static string Rewards(IReadOnlyList<ArcadeRewardSaveData> rewards)
        {
            if (rewards == null || rewards.Count == 0) return Loc.T("ui.arcade.none");
            return string.Join("\n", rewards
                .Where(value => value != null && value.Amount > 0)
                .GroupBy(value => value.RewardId, StringComparer.Ordinal)
                .Select(group => $"{ProductName(group.Key)} × {group.Sum(value => value.Amount)}"));
        }

        public static string ProductName(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return Loc.T("ui.arcade.none");
            var key = "ui.arcade.product." + productId + ".name";
            var value = Loc.T(key);
            return string.Equals(value, key, StringComparison.Ordinal) ? productId : value;
        }


        /// <summary>把联机街机领域结果转换为玩家可读提示；未知异常保留统一兜底，不泄漏内部枚举名。</summary>
        public static string OnlineResult(OnlineArcadeResultCode code)
        {
            return code switch
            {
                OnlineArcadeResultCode.None => string.Empty,
                OnlineArcadeResultCode.Success => Loc.T("ui.online.success"),
                OnlineArcadeResultCode.NotLoggedIn => Loc.T("ui.online.error.not_logged_in"),
                OnlineArcadeResultCode.InvalidState => Loc.T("ui.arcade.online.error.state"),
                OnlineArcadeResultCode.SaveFailed => Loc.T("ui.arcade.online.error.save"),
                OnlineArcadeResultCode.OperationConflict => Loc.T("ui.arcade.online.error.operation"),
                OnlineArcadeResultCode.VersionMismatch => Loc.T("ui.arcade.online.error.version"),
                OnlineArcadeResultCode.OperationInProgress => Loc.T("ui.online.error.busy"),
                OnlineArcadeResultCode.Timeout => Loc.T("ui.online.error.request_timeout"),
                OnlineArcadeResultCode.Disconnected => Loc.T("ui.online.error.disconnected"),
                _ => Loc.T("ui.online.error.unknown"),
            };
        }


    }
}

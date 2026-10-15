using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存已经结算到基地的通用街机奖励余额与局 ID；负责恰好一次账本，不解释奖励的具体消费用途。
    /// </summary>
    public sealed class ArcadeRewardWalletModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string SaveSectionId = "another-day.arcade-rewards";
        private const int SchemaVersion = 1;
        private readonly Dictionary<string, long> balances = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<string> settledRunIds = new HashSet<string>(StringComparer.Ordinal);

        public string SectionId => SaveSectionId;
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;
        public IReadOnlyDictionary<string, long> Balances => balances;

        protected override void OnInit()
        {
        }

        public bool HasSettled(string runId) => !string.IsNullOrWhiteSpace(runId) && settledRunIds.Contains(runId);
        public long GetBalance(string rewardId) => !string.IsNullOrWhiteSpace(rewardId) && balances.TryGetValue(rewardId, out var value) ? value : 0;

        public bool Validate(JToken data, out string error)
        {
            // 旧基地档允许缺少新分区；下一次成功保存会补齐正式空账本。
            if (data == null || data.Type == JTokenType.Null)
            {
                error = null;
                return true;
            }

            if (!(data is JObject root) || root["schemaVersion"]?.Type != JTokenType.Integer || root["schemaVersion"].Value<int>() != SchemaVersion ||
                !(root["balances"] is JObject savedBalances) || !(root["settledRunIds"] is JArray savedIds))
            {
                error = "街机奖励分区结构无效";
                return false;
            }

            foreach (var property in savedBalances.Properties())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Value.Type != JTokenType.Integer ||
                    !TryReadNonNegativeInt64(property.Value, out _))
                {
                    error = "街机奖励余额无效";
                    return false;
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in savedIds)
            {
                var id = token.Type == JTokenType.String ? token.Value<string>() : null;
                if (!ArcadeRunIdentity.IsValid(id) || !ids.Add(id))
                {
                    error = "街机已结算局 ID 无效";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public JToken Capture()
        {
            var savedBalances = new JObject();
            foreach (var pair in balances) savedBalances[pair.Key] = pair.Value;
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["balances"] = savedBalances,
                ["settledRunIds"] = new JArray(settledRunIds)
            };
        }

        public void Restore(JToken data)
        {
            Reset();
            if (data == null || data.Type == JTokenType.Null) return;
            foreach (var property in ((JObject)data["balances"]).Properties()) balances[property.Name] = property.Value.Value<long>();
            foreach (var token in (JArray)data["settledRunIds"]) settledRunIds.Add(token.Value<string>());
        }

        public void Reset()
        {
            balances.Clear();
            settledRunIds.Clear();
        }

        /// <summary>在内存中原子累加一局奖励；同一 RunId 重试只返回 alreadySettled，不重复计入。</summary>
        internal bool TryApply(string runId, IReadOnlyList<ArcadeRewardSaveData> rewards, out bool alreadySettled, out string error)
        {
            alreadySettled = HasSettled(runId);
            if (alreadySettled)
            {
                error = null;
                return true;
            }

            if (!ArcadeRunIdentity.IsValid(runId))
            {
                error = "街机局 ID 无效";
                return false;
            }

            var candidate = new Dictionary<string, long>(balances, StringComparer.Ordinal);
            try
            {
                foreach (var reward in rewards ?? Array.Empty<ArcadeRewardSaveData>())
                {
                    if (reward == null || string.IsNullOrWhiteSpace(reward.RewardId) || reward.Amount <= 0)
                    {
                        error = "待结算街机奖励无效";
                        return false;
                    }

                    candidate.TryGetValue(reward.RewardId, out var oldValue);
                    candidate[reward.RewardId] = checked(oldValue + reward.Amount);
                }
            }
            catch (OverflowException)
            {
                error = "街机奖励数量溢出";
                return false;
            }

            balances.Clear();
            foreach (var pair in candidate) balances[pair.Key] = pair.Value;
            settledRunIds.Add(runId);
            error = null;
            return true;
        }

        private static bool TryReadNonNegativeInt64(JToken token, out long value)
        {
            try
            {
                value = token.Value<long>();
                return value >= 0;
            }
            catch (Exception)
            {
                value = 0;
                return false;
            }
        }
    }
}

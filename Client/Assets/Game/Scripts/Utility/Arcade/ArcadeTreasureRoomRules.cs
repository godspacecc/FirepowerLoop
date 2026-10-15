using System;
using System.Collections.Generic;
using System.Linq;
#if DOTNET
using RoomConfig = ET.ArcadeTreasureRoomConfig;
#else
using RoomConfig = global::ArcadeTreasureRoomConfig;
#endif

namespace AnotherDay.Arcade
{
    public enum ArcadeTreasureRewardKind { GoldBar = 1, Gem = 2, Bomb = 3, Dash = 4 }

    /// <summary>实例内稳定的不可变物品参数；坐标为房间米，不含 Unity 资源或领取者状态。</summary>
    public readonly struct ArcadeTreasureRewardParameters
    {
        public ArcadeTreasureRewardParameters(int id, ArcadeTreasureRewardKind kind, float x, float y,
            int points, int progress, int amount)
        { Id = id; Kind = kind; X = x; Y = y; Points = points; Progress = progress; Amount = amount; }
        public int Id { get; }
        public ArcadeTreasureRewardKind Kind { get; }
        public float X { get; }
        public float Y { get; }
        public int Points { get; }
        public int Progress { get; }
        public int Amount { get; }
    }

    /// <summary>双端配置转换后共享的不可变房间合同；值由 Luban 源决定，不替代局或玩家权威。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeTreasureRoomParameters
    {
        private ArcadeTreasureRoomParameters(RoomConfig row)
        {
            Id = row.Id; ArenaConfigNumber = row.ArenaConfigNumber; WaveNumber = row.WaveNumber;
            Duration = row.Duration; EntryX = row.EntryX; EntryY = row.EntryY; EntryRadius = row.EntryRadius;
            ReturnX = row.ReturnX; ReturnY = row.ReturnY; HalfWidth = row.HalfWidth; HalfHeight = row.HalfHeight;
            SpawnX = row.SpawnX; SpawnY = row.SpawnY; ExitX = row.ExitX; ExitY = row.ExitY;
            ExitRadius = row.ExitRadius; PickupRadius = row.PickupRadius;
            ProgressPerLevel = row.ProgressPerLevel; MaxMultiplier = row.MaxMultiplier;
            LifeThreshold = row.LifeThreshold; MaxLives = row.MaxLives;
            Rewards = Array.AsReadOnly(row.Rewards.Select(r => new ArcadeTreasureRewardParameters(r.Id,
                (ArcadeTreasureRewardKind)(int)r.Kind, r.X, r.Y, r.Points, r.Progress, r.Amount)).ToArray());
        }
        public string Id { get; }
        public int ArenaConfigNumber { get; }
        public int WaveNumber { get; }
        public float Duration { get; }
        public float EntryX { get; }
        public float EntryY { get; }
        public float EntryRadius { get; }
        public float ReturnX { get; }
        public float ReturnY { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }
        public float SpawnX { get; }
        public float SpawnY { get; }
        public float ExitX { get; }
        public float ExitY { get; }
        public float ExitRadius { get; }
        public float PickupRadius { get; }
        public int ProgressPerLevel { get; }
        public int MaxMultiplier { get; }
        public long LifeThreshold { get; }
        public int MaxLives { get; }
        public IReadOnlyList<ArcadeTreasureRewardParameters> Rewards { get; }

        /// <summary>目录加载时验证入口与返回点可达，房内出生及出口留足角色半径；不执行运行时碰撞。</summary>
        public bool FitsArena(float halfWidth, float halfHeight, float playerRadius)
        {
            if (float.IsNaN(halfWidth) || float.IsInfinity(halfWidth) || float.IsNaN(halfHeight) || float.IsInfinity(halfHeight) ||
                float.IsNaN(playerRadius) || float.IsInfinity(playerRadius) || halfWidth <= 0 || halfHeight <= 0 || playerRadius <= 0) return false;
            return Math.Abs(EntryX) + EntryRadius <= halfWidth && Math.Abs(EntryY) + EntryRadius <= halfHeight &&
                Math.Abs(ReturnX) + playerRadius <= halfWidth && Math.Abs(ReturnY) + playerRadius <= halfHeight &&
                Math.Abs(SpawnX) + playerRadius <= HalfWidth && Math.Abs(SpawnY) + playerRadius <= HalfHeight &&
                Math.Abs(ExitX) + Math.Max(ExitRadius, playerRadius) <= HalfWidth && Math.Abs(ExitY) + Math.Max(ExitRadius, playerRadius) <= HalfHeight;
        }

        /// <summary>启动目录时复制并验证全部参数，拒绝不完整房间而不是静默生成空奖励。</summary>
        public static ArcadeTreasureRoomParameters FromConfig(RoomConfig row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var p = new ArcadeTreasureRoomParameters(row);
            var numbers = new[] { p.Duration, p.EntryX, p.EntryY, p.EntryRadius, p.ReturnX, p.ReturnY,
                p.HalfWidth, p.HalfHeight, p.SpawnX, p.SpawnY, p.ExitX, p.ExitY, p.ExitRadius, p.PickupRadius };
            if (numbers.Any(v => float.IsNaN(v) || float.IsInfinity(v)) || string.IsNullOrWhiteSpace(p.Id) ||
                p.ArenaConfigNumber <= 0 || p.WaveNumber <= 0 || p.Duration <= 0 || p.EntryRadius <= 0 ||
                p.HalfWidth <= 0 || p.HalfHeight <= 0 || p.ExitRadius <= 0 || p.PickupRadius <= 0 ||
                p.ProgressPerLevel <= 0 || p.MaxMultiplier < 1 || p.LifeThreshold <= 0 || p.MaxLives < 1 ||
                p.Rewards.Count == 0 || p.Rewards.Select(r => r.Id).Distinct().Count() != p.Rewards.Count ||
                Math.Abs(p.SpawnX) > p.HalfWidth || Math.Abs(p.SpawnY) > p.HalfHeight ||
                Math.Abs(p.ExitX) > p.HalfWidth || Math.Abs(p.ExitY) > p.HalfHeight ||
                p.Rewards.Any(r => r.Id <= 0 || !Enum.IsDefined(typeof(ArcadeTreasureRewardKind), r.Kind) ||
                    float.IsNaN(r.X) || float.IsInfinity(r.X) || float.IsNaN(r.Y) || float.IsInfinity(r.Y) ||
                    Math.Abs(r.X) + p.PickupRadius > p.HalfWidth || Math.Abs(r.Y) + p.PickupRadius > p.HalfHeight ||
                    r.Points < 0 || r.Progress < 0 || r.Amount <= 0))
                throw new ArgumentException("宝藏房参数、位置或共享奖励无效：" + p.Id);
            return p;
        }
    }

    /// <summary>只拥有本场房间的成员、共享时钟和物品账本；领域所有者负责资源事务、坐标及持久化。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeTreasureRoomState
    {
        private readonly HashSet<long> members = new HashSet<long>();
        private readonly Dictionary<int, ArcadeTreasureRewardParameters> remaining;
        private bool applying;
        public ArcadeTreasureRoomState(string instanceId, ArcadeTreasureRoomParameters parameters)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException("区域身份为空");
            InstanceId = instanceId; Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            remaining = parameters.Rewards.ToDictionary(r => r.Id);
            RemainingSeconds = parameters.Duration;
        }
        public string InstanceId { get; }
        public ArcadeTreasureRoomParameters Parameters { get; }
        public bool Started { get; private set; }
        public bool Expired { get; private set; }
        /// <summary>秒；首次进入前保留完整时限，所有成员离开后仍继续减少。</summary>
        public double RemainingSeconds { get; private set; }
        public long[] Members => members.OrderBy(id => id).ToArray();
        public ArcadeTreasureRewardParameters[] RemainingRewards => remaining.Values.OrderBy(r => r.Id).ToArray();
        public bool Contains(long playerId) => members.Contains(playerId);
        private bool Valid(string instanceId, long playerId) =>
            !applying && !Expired && InstanceId == instanceId && playerId > 0;

        public bool TryEnter(string instanceId, long playerId, bool discovered, bool cleared, out string error)
        {
            if (!Valid(instanceId, playerId) || !discovered || !cleared)
            { error = "宝藏入口身份、解锁或时限无效"; return false; }
            members.Add(playerId); Started = true; error = null; return true;
        }
        public bool TryLeave(string instanceId, long playerId, out string error)
        {
            if (!Valid(instanceId, playerId) || !members.Remove(playerId))
            { error = "玩家不在当前宝藏房内"; return false; }
            error = null; return true;
        }

        /// <summary>仅活动权威帧推进；暂停不调用，过期成员仅返回一次，调用方负责恢复竞技场位置。</summary>
        public long[] Advance(float seconds)
        {
            if (applying || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Started || Expired) return Array.Empty<long>();
            RemainingSeconds = Math.Max(0, RemainingSeconds - seconds);
            if (RemainingSeconds > 0) return Array.Empty<long>();
            Expired = true;
            var result = Members; members.Clear(); return result;
        }

        /// <summary>同一权威线程同步领取；apply 必须先构造资源候选，失败/异常不消耗物品，禁止异步和回调重入。</summary>
        public bool TryCollect(string instanceId, long playerId, int rewardId,
            Func<ArcadeTreasureRewardParameters, bool> apply, out string error)
        {
            if (!Valid(instanceId, playerId) || !members.Contains(playerId) ||
                !remaining.TryGetValue(rewardId, out var reward) || apply == null)
            { error = "宝藏领取身份或物品已失效"; return false; }
            applying = true;
            try
            {
                if (!apply(reward)) { error = "宝藏奖励未能应用"; return false; }
                remaining.Remove(rewardId); error = null; return true;
            }
            finally { applying = false; }
        }
    }

    /// <summary>个人累计分、倍率与命数的值快照；不含购物现金、基地奖励或共享队友资源。</summary>
    public readonly struct ArcadeTreasureScoreState
    {
        public ArcadeTreasureScoreState(long score, int multiplier, int progress, int lives)
        { Score = score; Multiplier = multiplier; Progress = progress; Lives = lives; }
        public long Score { get; }
        public int Multiplier { get; }
        public int Progress { get; }
        public int Lives { get; }
        public ArcadeTreasureScoreState ResetAfterDeath() => new ArcadeTreasureScoreState(Score, 1, 0, Lives);
    }

    /// <summary>双端共用的纯积分候选计算，不修改账本、玩家记录、现金或存档。</summary>
    public static class ArcadeTreasureScoreRules
    {
        /// <summary>本项目确认的普通击杀：100×当前倍率，只跨累计分档加命，不增长财宝进度。</summary>
        public static bool TryAwardKill(ArcadeTreasureScoreState current, ArcadeTreasureRoomParameters rules,
            out ArcadeTreasureScoreState next)
        {
            next = current;
            if (rules == null || current.Score < 0 || current.Multiplier < 1 || current.Multiplier > rules.MaxMultiplier ||
                current.Progress < 0 || current.Progress >= rules.ProgressPerLevel || current.Lives < 0) return false;
            try
            {
                long score = checked(current.Score + 100L * current.Multiplier);
                long tiers = score / rules.LifeThreshold - current.Score / rules.LifeThreshold;
                int lives = current.Lives >= rules.MaxLives ? current.Lives :
                    current.Lives + (int)Math.Min(tiers, rules.MaxLives - current.Lives);
                next = new ArcadeTreasureScoreState(score, current.Multiplier, current.Progress, lives); return true;
            }
            catch (OverflowException) { return false; }
        }

        /// <summary>领取前倍率计分，再增长倍率；累计档位计命，不扣积分，满命奖励不排队，溢出整次失败。</summary>
        public static bool TryAward(ArcadeTreasureScoreState current, ArcadeTreasureRewardParameters reward,
            ArcadeTreasureRoomParameters rules, out ArcadeTreasureScoreState next)
        {
            next = current;
            if (rules == null || current.Score < 0 || current.Multiplier < 1 ||
                current.Multiplier > rules.MaxMultiplier || current.Progress < 0 ||
                current.Progress >= rules.ProgressPerLevel || current.Lives < 0 || reward.Points < 0 || reward.Progress < 0)
                return false;
            try
            {
                long score = checked(current.Score + checked((long)reward.Points * current.Multiplier));
                long tiers = score / rules.LifeThreshold - current.Score / rules.LifeThreshold;
                int lives = current.Lives >= rules.MaxLives ? current.Lives :
                    current.Lives + (int)Math.Min(tiers, rules.MaxLives - current.Lives);
                long progress = (long)current.Progress + reward.Progress;
                int multiplier = (int)Math.Min(rules.MaxMultiplier, current.Multiplier + progress / rules.ProgressPerLevel);
                next = new ArcadeTreasureScoreState(score, multiplier,
                    multiplier == rules.MaxMultiplier ? 0 : (int)(progress % rules.ProgressPerLevel), lives);
                return true;
            }
            catch (OverflowException) { return false; }
        }
    }
}

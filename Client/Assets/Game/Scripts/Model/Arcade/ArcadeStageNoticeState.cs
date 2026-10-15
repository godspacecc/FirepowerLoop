using System;

namespace Game
{
    /// <summary>阶段提示的纯表现类型；不参与战斗阶段或离场裁决。</summary>
    internal enum ArcadeStageNoticeKind { WaveStarted, WaveCompleted, ArenaCompleted }

    /// <summary>固定截止时刻的短暂横幅快照；刷新文字、重建 HUD 都不会延长显示。</summary>
    internal sealed class ArcadeStageNotice
    {
        internal const double LifetimeSeconds = 1.8;
        private const double FadeSeconds = .45;
        public ArcadeStageNoticeKind Kind { get; }
        public int WaveNumber { get; }
        public double ExpiresAt { get; }
        /// <summary>创建一次提示的固定截止时间；HUD 消费不会改写它。</summary>
        public ArcadeStageNotice(ArcadeStageNoticeKind kind, int waveNumber, double now)
        { Kind = kind; WaveNumber = waveNumber; ExpiresAt = now + LifetimeSeconds; }
        /// <summary>只计算视觉淡出，不推进或读取战斗计时。</summary>
        public float Opacity(double now) => (float)Math.Max(0, Math.Min(1, (ExpiresAt - now) / FadeSeconds));
    }

    /// <summary>战斗会话拥有的阶段去重记录；HUD 只能消费一次，最高阶段替换旧提示而不排队。</summary>
    internal sealed class ArcadeStageNoticeState
    {
        // 当前战斗身份与已观察到的最高阶段，只供表现去重，不写入存档。
        private object session;
        private int latestStage;
        // 唯一可消费提示；后来的阶段替换旧提示，避免两条横幅重叠。
        private ArcadeStageNotice current;
        private bool pending;

        /// <summary>读取已提交阶段；重复、倒退快照不会重播，场次完成优先于同一波的清波。</summary>
        public bool Observe(object battleSession, int arenaNumber, int waveNumber, bool waveCleared,
            bool arenaCompleted, double now)
        {
            if (arenaNumber != 1 || waveNumber <= 0) { Clear(); return false; }
            if (!Equals(session, battleSession)) { Clear(); session = battleSession; }
            int stage = arenaCompleted ? int.MaxValue : waveNumber * 2 - (waveCleared ? 0 : 1);
            if (stage <= latestStage) return false;
            latestStage = stage;
            current = new ArcadeStageNotice(arenaCompleted ? ArcadeStageNoticeKind.ArenaCompleted :
                waveCleared ? ArcadeStageNoticeKind.WaveCompleted : ArcadeStageNoticeKind.WaveStarted, waveNumber, now);
            pending = true;
            return true;
        }

        /// <summary>隐藏时未消费的提示也遵守原截止时刻；已消费提示不能因 HUD 重开而重播。</summary>
        public bool TryConsume(double now, out ArcadeStageNotice notice)
        {
            notice = null;
            if (!pending) return false;
            pending = false;
            if (current == null || current.Opacity(now) <= 0) return false;
            notice = current;
            return true;
        }

        /// <summary>退出、重试或更换战斗会话时释放旧提示；不修改任何战斗状态。</summary>
        public void Clear() { session = null; latestStage = 0; current = null; pending = false; }
    }

    /// <summary>新阶段提示已提交；事件重复发送仍只能消费同一记录一次。</summary>
    internal readonly struct ArcadeStageNoticeChangedEvent { }
}

using QFramework;

namespace Game
{
    /// <summary>
    /// 持有当前街机局的权威稳定数据和运行阶段；不拥有战斗现场，也不直接读写文件或发放基地奖励。
    /// </summary>
    public sealed class ArcadeRunModel : AbstractModel
    {
        private ArcadeRunSaveData current;

        public ArcadeRunPhase Phase { get; private set; } = ArcadeRunPhase.None;
        public bool HasRun => current != null;
        public ArcadeRunSaveData Current => current?.Clone();

        protected override void OnInit()
        {
        }

        internal void CommitSafeNode(ArcadeRunSaveData value)
        {
            current = value?.Clone();
            Phase = current == null ? ArcadeRunPhase.None : current.Victory ? ArcadeRunPhase.Victory : current.HasResult ? ArcadeRunPhase.Result : ArcadeRunPhase.SafeShop;
        }

        internal void EnterArena()
        {
            if (current != null) Phase = ArcadeRunPhase.Arena;
        }

        internal void EnterChallenge()
        {
            if (current != null) Phase = ArcadeRunPhase.Challenge;
        }

        /// <summary>竞技场内生命变化只改运行态；回到安全节点时会被最近落盘快照覆盖。</summary>
        internal void SetTransientLives(int value)
        {
            if (current != null) current.RemainingLives = value;
        }

        /// <summary>领取成功后的本场个人值只更新现场，不写安全节点；失败候选不得调用。</summary>
        internal void SetTransientTreasureScore(AnotherDay.Arcade.ArcadeTreasureScoreState value)
        {
            if (current == null) return;
            current.Score = value.Score; current.ScoreMultiplier = value.Multiplier;
            current.MultiplierProgress = value.Progress; current.RemainingLives = value.Lives;
        }

        internal void Clear()
        {
            current = null;
            Phase = ArcadeRunPhase.None;
        }
    }
}

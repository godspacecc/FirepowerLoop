using System;
using QFramework;
namespace Game
{
    /// <summary>独立幸存者状态通知；界面从模型读取当前快照。</summary>
    public readonly struct SurvivorChangedEvent { }

    /// <summary>幸存者展示快照，不向界面暴露可变规则状态。</summary>
    public readonly struct SurvivorHudSnapshot
    {
        public readonly bool Active, Paused, Finished;
        public readonly int Health, MaximumHealth, Level, Kills, ChoiceRevision;
        public readonly long Experience, RequiredExperience, Score;
        public readonly double SurvivalSeconds;
        public SurvivorHudSnapshot(bool active, bool paused, SurvivorRunState state, int health, int maximumHealth, int kills, long score)
        {
            Active=active; Paused=paused; Finished=state?.Finished??false;
            Health=health; MaximumHealth=maximumHealth; Kills=kills; Score=score;
            Level=state?.Level??1; ChoiceRevision=state?.ChoiceRevision??0;
            Experience=state?.Experience??0; RequiredExperience=state?.RequiredExperience??0;
            SurvivalSeconds=state?.ElapsedSeconds??0;
        }
    }

    /// <summary>幸存者局权威数据，与闯关保存模型完全隔离。</summary>
    public sealed class SurvivorModel : AbstractModel
    {
        /// <summary>规则状态仅由幸存者系统写入。</summary>
        internal SurvivorRunState State;
        /// <summary>当前局独立武器槽，仅由幸存者系统写入。</summary>
        internal SurvivorLoadout Loadout;
        /// <summary>界面只读查询武器等级。</summary>
        public int WeaponLevel(string kind)=>Loadout?.LevelOf(kind)??0;
        /// <summary>包含主武器的总槽数与当前占用。</summary>
        public int WeaponSlotCapacity=>Loadout?.Capacity??0;
        public int UsedWeaponSlots=>Loadout?.UsedSlots??0;
        public string[] EquippedWeapons=>Loadout?.Weapons??Array.Empty<string>();
        /// <summary>界面展示值由系统事务提交。</summary>
        internal bool Active, Paused;
        internal int Health, MaximumHealth, Kills;
        internal long Score;
        /// <summary>结束结果内部保留，外部读取副本。</summary>
        internal SurvivorResultData Result;
        /// <summary>当前可选强化编号，每次读取返回副本。</summary>
        internal int[] Choices=Array.Empty<int>();
        public SurvivorHudSnapshot Snapshot => new SurvivorHudSnapshot(Active,Paused,State,Health,MaximumHealth,Kills,Score);
        public int[] UpgradeChoices => (int[])Choices.Clone();
        public SurvivorResultData ResultSnapshot => Result==null?null:new SurvivorResultData { RunId=Result.RunId,Score=Result.Score,SurvivalSeconds=Result.SurvivalSeconds,Kills=Result.Kills,Level=Result.Level,Outcome=Result.Outcome };
        protected override void OnInit() { }
    }
}

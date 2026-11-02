using System;
using QFramework;
namespace Game
{
    /// <summary>独立幸存者状态通知；界面从模型读取当前快照。</summary>
    public readonly struct SurvivorChangedEvent { }
    /// <summary>实际生命损失后的瞬时HUD提示，首次同步、治疗和无敌均不发送。</summary>
    public readonly struct SurvivorPlayerHurtEvent { }

    /// <summary>幸存者展示快照，不向界面暴露可变规则状态。</summary>
    public readonly struct SurvivorHudSnapshot
    {
        public readonly bool Active, Paused, Finished;
        public readonly int Health, MaximumHealth, Level, Kills, ChoiceRevision;
        /// <summary>三个卡槽共享的剩余刷新次数。</summary>
        public readonly int RemainingRefreshes;
        public readonly long Experience, RequiredExperience, Score;
        public readonly double SurvivalSeconds;
        public SurvivorHudSnapshot(bool active, bool paused, SurvivorRunState state, int health, int maximumHealth, int kills, long score)
        {
            Active=active; Paused=paused; Finished=state?.Finished??false;
            Health=health; MaximumHealth=maximumHealth; Kills=kills; Score=score;
            RemainingRefreshes=state?.RemainingRefreshes??0;
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
        /// <summary>菜单已验证选择和本局外观；Stop保留以支持重开。</summary>
        public int SelectedCharacter {get;internal set;}
        public int RunCharacter {get;internal set;}
        /// <summary>本局步枪弹夹与已开始的换弹事务，仅由幸存者系统推进。</summary>
        internal int RifleRounds, RifleCapacity;
        internal float RifleReloadRemaining, RifleReloadDuration;
        /// <summary>供既有界面或检查读取的弹量快照，不暴露可写字段。</summary>
        public int RifleAmmo => RifleRounds;
        public int RifleMagazineSize => RifleCapacity;
        public float RifleReloadTimeRemaining => RifleReloadRemaining;
        /// <summary>只读换弹完成比例，供武器图标外圈显示；不推进换弹时间。</summary>
        public float RifleReloadProgress => RifleReloadDuration>0?UnityEngine.Mathf.Clamp01(1-RifleReloadRemaining/RifleReloadDuration):0f;
        /// <summary>当前局独立武器槽，仅由幸存者系统写入。</summary>
        internal SurvivorLoadout Loadout;
        /// <summary>导弹使用独立发射冷却，与步枪弹药和射速无关。</summary>
        internal float RocketClock, RocketInterval;
        public float SupportCooldownTimeRemaining(string kind) => kind=="RocketSupport" ? RocketClock : 0;
        public float SupportCooldownProgress(string kind) => kind=="RocketSupport" && RocketInterval>0
            ? UnityEngine.Mathf.Clamp01(1-RocketClock/RocketInterval) : 1;
        /// <summary>系统提交的燃烧弹冷却显示快照；HUD不自行推进时间。</summary>
        internal float MolotovCooldownRemaining, MolotovCooldownDuration;
        public float MolotovCooldownTimeRemaining => MolotovCooldownRemaining;
        /// <summary>冷却结束保持满圈，无目标等待时也可明确看到已就绪。</summary>
        public float MolotovCooldownProgress => MolotovCooldownRemaining<=0?1f:
            MolotovCooldownDuration>0?UnityEngine.Mathf.Clamp01(1-MolotovCooldownRemaining/MolotovCooldownDuration):0f;
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
        /// <summary>本局已落盘金币与累计余额，不从旧积分兑换。</summary>
        internal long RunGold, GoldBalance;
        public long CollectedGold => RunGold;
        public long TotalGold => GoldBalance;
        /// <summary>结束结果内部保留，外部读取副本。</summary>
        internal SurvivorResultData Result;
        /// <summary>当前可选强化编号，每次读取返回副本。</summary>
        internal int[] Choices=Array.Empty<int>();
        public SurvivorHudSnapshot Snapshot => new SurvivorHudSnapshot(Active,Paused,State,Health,MaximumHealth,Kills,Score);
        public int[] UpgradeChoices => (int[])Choices.Clone();
        public SurvivorResultData ResultSnapshot => Result==null?null:new SurvivorResultData { RunId=Result.RunId,Score=Result.Score,Gold=Result.Gold,SurvivalSeconds=Result.SurvivalSeconds,Kills=Result.Kills,Level=Result.Level,Outcome=Result.Outcome };
        protected override void OnInit() { }
    }
}

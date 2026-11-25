using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>Boss 技能瞬时阶段；只存在于当前竞技场，联机协议使用相同数值。</summary>
    public enum ArcadeBossPhase
    {
        None = 0,
        Telegraph = 1,
        Charging = 2,
        RayTelegraph = 3,
    }

    internal sealed class ArcadeEnemyRuntime
    {
        /// <summary>码头海路活动状态，回池清零。</summary>
        public FirepowerLoop.Arcade.ArcadeDockSeaEntryState DockSeaEntry;
        /// <summary>普通敌人的场外接近状态，回池清零，不改变受伤资格。</summary>
        public FirepowerLoop.Arcade.ArcadeOutsideEntryState OutsideEntry;
        /// <summary>幸存者场内爬出进度，其他玩法保持零值。</summary>
        public SurvivorGroundEntryState GroundEntry;
        /// <summary>存活目标资格；身体是否与攻击相交由各空间命中入口裁决。</summary>
        public bool CanBeAttacked => Health > 0;
        /// <summary>当前地图特色敌人的现身及一次重生状态，不写入轮前存档。</summary>
        public FirepowerLoop.Arcade.ArcadeStageEnemyState StageEnemy;
        /// <summary>当前地图牛撞的权威飞行与落地僵直；对象回池必须归零。</summary>
        public FirepowerLoop.Arcade.ArcadeHerdImpactState HerdImpact;
        /// <summary>真实火焰命中后的短暂表现计时，不造成持续伤害。</summary>
        public float FlameHitRemainingSeconds;
        /// <summary>最后一次有效伤害是否来自喷火器，仅用于死亡表现。</summary>
        public bool LastDamageWasFlame;
        /// <summary>当前敌人在同来源残火内的接触时间；离开覆盖后重新计时。</summary>
        public float EvolutionBurnContactSeconds;
        public int Id;
        public string ConfigId;
        /// <summary>出生时从权威配置固定的禁紫资格，强化生命不会变为高级掉落。</summary>
        public bool BlocksPurpleSkullDrop;
        public global::EnemyAttackKind AttackKind;
        public Vector2 Position;
        public int Health;
        public int MaxHealth;
        /// <summary>当前轮独立护甲耐久，归还对象池时清零，不计入生命或存档。</summary>
        public int ArmorHealth;
        /// <summary>背桶独立耐久；零表示尚未命中，卸桶转交地面桶并在回池清零。</summary>
        public int CarriedBarrelHealth;
        public int Damage;
        public float Speed;
        public float AttackRange;
        public float AttackInterval;
        public float AttackCooldown;
        /// <summary>普通近战的配置前摇及当前剩余秒数，仅进入攻击距离后推进。</summary>
        public float MeleeWindupSeconds, MeleeWindupRemaining;
        /// <summary>命中后的权威收招剩余量，表现直接采样，回池清零。</summary>
        public float MeleeRecoveryRemaining;
        /// <summary>本次近战已经开始等待，离开范围、受控或回池立即取消。</summary>
        public bool MeleeWindupActive;
        /// <summary>标记当前波次中的 Boss，只供常驻血条等表现读取，不参与伤害或行为判断。</summary>
        public bool IsBossPresentation;
        /// <summary>每次规则层接受攻击时递增；仅用于让表现层去重播放攻击动画。</summary>
        public int AttackSequence;
        /// <summary>本轮特殊敌人与独立Boss身份，不进入本局文件。</summary>
        public int ContentSpecialKind, ContentBossKind;
        /// <summary>固定出生序已赋值，重复绑定Boss不重复缩放生命。</summary>
        public bool ContentAssigned, ContentWarning, ContentEvolved;
        /// <summary>出生、最后受击与眩晕的活动时刻。</summary>
        public double ContentBornAt, ContentLastHitAt = -1, ContentStunUntil;
        /// <summary>技能预警锁定的方向及人偶已破坏阶段。</summary>
        public Vector2 ContentLockedDirection, ContentLockedTarget;
        public int ContentMannequinHits, ContentSkillCycle = -1, ContentTelegraphCycle = -1;
        /// <summary>本活动帧附近祭司提供的移动和攻击增益。</summary>
        public float ContentAllyBuff = 1;
        public string BossId;
        public ArcadeBossDefinition BossDefinition;
        public ArcadeBossPhase BossPhase;
        public float BossPhaseRemaining;
        public float BossSkillCooldown;
        public Vector2 BossChargeDirection;
        public bool BossImpactConsumed;
        /// <summary>射线独立冷却（秒）；预警方向在进入 RayTelegraph 时锁定。</summary>
        public float BossRayCooldown;

        /// <summary>归还规则对象池前清除身份与战斗状态，避免下一个实体继承旧值。</summary>
        public void Reset()
        {
            DockSeaEntry = default;OutsideEntry=default;GroundEntry=default;
            StageEnemy=default;
            EvolutionBurnContactSeconds=0;
            FlameHitRemainingSeconds=0;LastDamageWasFlame=false;
            Id = 0;
            ConfigId = null;
            BlocksPurpleSkullDrop = false;
            AttackKind = default;
            Position = default;
            Health = 0;
            MaxHealth = 0;
            ArmorHealth = 0;CarriedBarrelHealth=0;
            Damage = 0;
            Speed = 0f;
            AttackRange = 0f;
            AttackInterval = 0f;
            AttackCooldown = 0f;
            MeleeWindupSeconds = MeleeWindupRemaining = 0f;
            MeleeRecoveryRemaining = 0f;
            MeleeWindupActive = false;
            IsBossPresentation = false;
            AttackSequence = 0;
            ContentSpecialKind = ContentBossKind = ContentMannequinHits = 0;
            ContentAssigned = ContentWarning = ContentEvolved = false;
            ContentBornAt = ContentStunUntil = 0;
            HerdImpact=default;
            ContentLastHitAt = -1;
            ContentSkillCycle = ContentTelegraphCycle = -1;
            ContentLockedDirection = ContentLockedTarget = default;
            ContentAllyBuff = 1;
            BossId = null;
            BossDefinition = default;
            BossPhase = ArcadeBossPhase.None;
            BossPhaseRemaining = 0f;
            BossSkillCooldown = 0f;
            BossChargeDirection = default;
            BossImpactConsumed = false;
            BossRayCooldown = 0f;
        }
    }

    internal sealed class ArcadeProjectileRuntime
    {
        public int Id;
        /// <summary>玩家弹生成时的强化层级与追踪身份，换枪不改变；回池清零。</summary>
        public int WeaponPowerLevel,TrackingEnemyId;
        /// <summary>本弹齐射身份、额外宽度与子榴弹标记，全部随回池清理。</summary>
        public int EvolutionVolleyId;
        public HashSet<int> EvolutionPushedEnemyIds;
        public float EvolutionHitRadius;
        public bool EvolutionClusterChild;
        /// <summary>紫射线发射时固定的反射余量，回池清零。</summary>
        public int RemainingReflections;
        /// <summary>追踪弹转速和寄生弹剩余减速秒数，由本弹的生成方固定。</summary>
        public float ContentTurnRate, ContentSlowSeconds;
        public bool Hostile;
        public Vector2 Position;
        public Vector2 Velocity;
        public int Damage;
        public float RemainingSeconds;
        public global::ArcadeWeaponBehavior Behavior;
        /// <summary>仅供表现层选择模型的配置资源名；不参与伤害、命中或寿命规则。</summary>
        public string ProjectileVisualResourceName;
        /// <summary>仅标记玩家武器弹丸，用于客户端枪口衔接；支援和敌方弹丸不使用该表现。</summary>
        public bool FromPlayerWeapon;
        /// <summary>生成时固定的武器配置身份，仅用于命中音色；换武器后在途弹丸仍保留原身份。</summary>
        public string SourceWeaponId;
        /// <summary>生成时固定的开枪位置，只用于死亡表现距离，回池清零。</summary>
        public Vector2 SourcePosition;
        /// <summary>购买支援的稳定来源，普通武器和普通掉落为空。</summary>
        public string SupportInstanceId;
        /// <summary>本弹丸尚可命中的不同敌人数；只存在于当前竞技场。</summary>
        public int RemainingHits;
        /// <summary>命中敌人或障碍时的爆炸半径，单位为竞技场米。</summary>
        public float ExplosionRadius;
        public readonly HashSet<int> HitEnemyIds = new HashSet<int>();

        public void Reset()
        {
            Id = 0;
            EvolutionPushedEnemyIds=null;EvolutionVolleyId=0;EvolutionHitRadius=0;EvolutionClusterChild=false;
            WeaponPowerLevel=TrackingEnemyId=RemainingReflections=0;
            ContentTurnRate = ContentSlowSeconds = 0;
            Hostile = false;
            Position = default;
            Velocity = default;
            Damage = 0;
            RemainingSeconds = 0f;
            Behavior = global::ArcadeWeaponBehavior.Direct;
            ProjectileVisualResourceName = null;
            FromPlayerWeapon = false;
            SourceWeaponId = null;
            SourcePosition = default;
            SupportInstanceId = null;
            RemainingHits = 0;
            ExplosionRadius = 0f;
            HitEnemyIds.Clear();
        }
    }

    internal sealed class ArcadeDropRuntime
    {
        /// <summary>只标记地图定点刷枪；投送完成前禁止拾取，不占用地面存留寿命。</summary>
        public bool WeaponAirdrop;
        public float DeliveryRemainingSeconds;
        public int Id;
        public ArcadeDropDefinition Definition;
        public Vector2 Position;
        /// <summary>地面存留秒数；所有来源统一30秒，暂停冻结，归零即回收。</summary>
        public float RemainingSeconds = 30f;

        public void Reset()
        {
            Id = 0;
            Definition = default;
            Position = default;
            WeaponAirdrop=false;DeliveryRemainingSeconds=0;
            RemainingSeconds = 30f;
        }
    }
}

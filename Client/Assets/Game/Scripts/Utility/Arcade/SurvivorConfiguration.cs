using System;
using System.Collections.Generic;
using System.Linq;
namespace FirepowerLoop.Arcade
{
    /// <summary>幸存者局只读配置，和闯关目录独立校验，读取失败拒绝开局。</summary>
    public sealed class SurvivorConfiguration
    {
        /// <summary>来自幸存者权威规则表的 Id。</summary>
        public string Id { get; private set; }
        /// <summary>来自幸存者权威规则表的 LayoutId。</summary>
        public string LayoutId { get; private set; }
        /// <summary>来自幸存者权威规则表的 EnemyId。</summary>
        public string EnemyId { get; private set; }
        /// <summary>来自幸存者权威规则表的 WeaponId。</summary>
        public string WeaponId { get; private set; }
        /// <summary>包含初始步枪的本局总武器槽数。</summary>
        public int WeaponSlots { get; private set; }
        /// <summary>步枪基础弹夹容量及自动换弹时长，数值来自权威规则表。</summary>
        public int RifleMagazineCapacity { get; private set; }
        public float RifleReloadSeconds { get; private set; }
        public float RifleMinimumReloadSeconds { get; private set; }
        /// <summary>独立武器逐级参数；不复用闯关限时效果的计时。</summary>
        public IReadOnlyList<SurvivorWeaponLevelDefinition> WeaponLevels { get; private set; }
        /// <summary>只允许读取表中真实存在的武器等级。</summary>
        public SurvivorWeaponLevelDefinition WeaponLevel(string kind,int level)=>WeaponLevels.Single(x=>x.Kind==kind&&x.Level==level);
        /// <summary>最大等级由连续配置行决定。</summary>
        public int MaximumWeaponLevel(string kind)=>WeaponLevels.Where(x=>x.Kind==kind).Max(x=>x.Level);
        /// <summary>来自幸存者权威规则表的 PlayerMaxHealth。</summary>
        public int PlayerMaxHealth { get; private set; }
        /// <summary>来自幸存者权威规则表的 PlayerMoveSpeed。</summary>
        public float PlayerMoveSpeed { get; private set; }
        /// <summary>来自幸存者权威规则表的 SpawnIntervalSeconds。</summary>
        public float SpawnIntervalSeconds { get; private set; }
        /// <summary>来自幸存者权威规则表的 MinimumSpawnIntervalSeconds。</summary>
        public float MinimumSpawnIntervalSeconds { get; private set; }
        /// <summary>来自幸存者权威规则表的 SpawnIntervalReductionPerMinute。</summary>
        public float SpawnIntervalReductionPerMinute { get; private set; }
        /// <summary>来自幸存者权威规则表的 EnemyHealthGrowthPerMinute。</summary>
        public float EnemyHealthGrowthPerMinute { get; private set; }
        /// <summary>来自幸存者权威规则表的 EnemyDamageGrowthPerMinute。</summary>
        public float EnemyDamageGrowthPerMinute { get; private set; }
        /// <summary>来自幸存者权威规则表的 EnemySpeedGrowthPerMinute。</summary>
        public float EnemySpeedGrowthPerMinute { get; private set; }
        /// <summary>来自幸存者权威规则表的 MaxAliveEnemies。</summary>
        public int MaxAliveEnemies { get; private set; }
        /// <summary>来自幸存者权威规则表的 MaxProjectiles。</summary>
        public int MaxProjectiles { get; private set; }
        /// <summary>来自幸存者权威规则表的 MaxDrops。</summary>
        public int MaxDrops { get; private set; }
        /// <summary>来自幸存者权威规则表的 InitialExperienceThreshold。</summary>
        public int InitialExperienceThreshold { get; private set; }
        /// <summary>来自幸存者权威规则表的 ExperienceThresholdGrowth。</summary>
        public int ExperienceThresholdGrowth { get; private set; }
        /// <summary>来自幸存者权威规则表的 ExperiencePerKill。</summary>
        public int ExperiencePerKill { get; private set; }
        /// <summary>经验掉率，0不掉落、1必掉。</summary>
        public float ExperienceDropChance { get; private set; }
        /// <summary>经验专用地面模型资源名，与金币表现独立。</summary>
        public string ExperienceVisualPrefabName { get; private set; }
        /// <summary>按场景物体资源约定解析后的经验模型逻辑地址。</summary>
        public string ExperienceVisualPrefabAddress { get; private set; }
        /// <summary>来自幸存者权威规则表的 ScorePerKill。</summary>
        public int ScorePerKill { get; private set; }
        /// <summary>来自幸存者权威规则表的 PickupRadius。</summary>
        public float PickupRadius { get; private set; }
        /// <summary>来自幸存者权威规则表的 MinimumSpawnDistance。</summary>
        public float MinimumSpawnDistance { get; private set; }
        /// <summary>来自幸存者权威规则表的 AimRange。</summary>
        public float AimRange { get; private set; }
        /// <summary>强化快照；MaxLevel为零表示可无限选择，效果由MaximumBonus渐近封顶。</summary>
        public IReadOnlyList<SurvivorUpgradeDefinition> Upgrades { get; private set; }
        /// <summary>从生成表复制并完整校验基础数值与引用，不暴露可变生成行。</summary>
        public static SurvivorConfiguration FromTables(global::Tables tables)
        {
            if (tables == null || tables.TbSurvivorRule.DataList.Count != 1) throw new ArgumentException("幸存者规则必须恰好一行");
            var r = tables.TbSurvivorRule.DataList[0];
            if(r.RifleMagazineCapacity<=0 || new[]{r.RifleReloadSeconds,r.RifleMinimumReloadSeconds}.Any(x=>float.IsNaN(x)||float.IsInfinity(x)||x<=0) || r.RifleMinimumReloadSeconds>r.RifleReloadSeconds)
                throw new ArgumentException("步枪弹夹容量与换弹时长配置无效");
            if (float.IsNaN(r.PlayerMaxHealth) || float.IsInfinity(r.PlayerMaxHealth) || r.PlayerMaxHealth <= 0) throw new ArgumentException("幸存者 PlayerMaxHealth 必须为有限正数");
            if (float.IsNaN(r.PlayerMoveSpeed) || float.IsInfinity(r.PlayerMoveSpeed) || r.PlayerMoveSpeed <= 0) throw new ArgumentException("幸存者 PlayerMoveSpeed 必须为有限正数");
            if (float.IsNaN(r.SpawnIntervalSeconds) || float.IsInfinity(r.SpawnIntervalSeconds) || r.SpawnIntervalSeconds <= 0) throw new ArgumentException("幸存者 SpawnIntervalSeconds 必须为有限正数");
            if (float.IsNaN(r.MinimumSpawnIntervalSeconds) || float.IsInfinity(r.MinimumSpawnIntervalSeconds) || r.MinimumSpawnIntervalSeconds <= 0) throw new ArgumentException("幸存者 MinimumSpawnIntervalSeconds 必须为有限正数");
            if (float.IsNaN(r.SpawnIntervalReductionPerMinute) || float.IsInfinity(r.SpawnIntervalReductionPerMinute) || r.SpawnIntervalReductionPerMinute <= 0) throw new ArgumentException("幸存者 SpawnIntervalReductionPerMinute 必须为有限正数");
            if (float.IsNaN(r.EnemyHealthGrowthPerMinute) || float.IsInfinity(r.EnemyHealthGrowthPerMinute) || r.EnemyHealthGrowthPerMinute < 0) throw new ArgumentException("幸存者 EnemyHealthGrowthPerMinute 必须为有限非负数");
            if (float.IsNaN(r.EnemyDamageGrowthPerMinute) || float.IsInfinity(r.EnemyDamageGrowthPerMinute) || r.EnemyDamageGrowthPerMinute < 0) throw new ArgumentException("幸存者 EnemyDamageGrowthPerMinute 必须为有限非负数");
            if (float.IsNaN(r.EnemySpeedGrowthPerMinute) || float.IsInfinity(r.EnemySpeedGrowthPerMinute) || r.EnemySpeedGrowthPerMinute < 0) throw new ArgumentException("幸存者 EnemySpeedGrowthPerMinute 必须为有限非负数");
            if (float.IsNaN(r.MaxAliveEnemies) || float.IsInfinity(r.MaxAliveEnemies) || r.MaxAliveEnemies <= 0) throw new ArgumentException("幸存者 MaxAliveEnemies 必须为有限正数");
            if (float.IsNaN(r.MaxProjectiles) || float.IsInfinity(r.MaxProjectiles) || r.MaxProjectiles <= 0) throw new ArgumentException("幸存者 MaxProjectiles 必须为有限正数");
            if (float.IsNaN(r.MaxDrops) || float.IsInfinity(r.MaxDrops) || r.MaxDrops <= 0) throw new ArgumentException("幸存者 MaxDrops 必须为有限正数");
            if (float.IsNaN(r.InitialExperienceThreshold) || float.IsInfinity(r.InitialExperienceThreshold) || r.InitialExperienceThreshold <= 0) throw new ArgumentException("幸存者 InitialExperienceThreshold 必须为有限正数");
            if (float.IsNaN(r.ExperienceThresholdGrowth) || float.IsInfinity(r.ExperienceThresholdGrowth) || r.ExperienceThresholdGrowth <= 0) throw new ArgumentException("幸存者 ExperienceThresholdGrowth 必须为有限正数");
            if (float.IsNaN(r.ExperiencePerKill) || float.IsInfinity(r.ExperiencePerKill) || r.ExperiencePerKill <= 0) throw new ArgumentException("幸存者 ExperiencePerKill 必须为有限正数");
            if (float.IsNaN(r.ScorePerKill) || float.IsInfinity(r.ScorePerKill) || r.ScorePerKill <= 0) throw new ArgumentException("幸存者 ScorePerKill 必须为有限正数");
            if (float.IsNaN(r.PickupRadius) || float.IsInfinity(r.PickupRadius) || r.PickupRadius <= 0) throw new ArgumentException("幸存者 PickupRadius 必须为有限正数");
            if (float.IsNaN(r.MinimumSpawnDistance) || float.IsInfinity(r.MinimumSpawnDistance) || r.MinimumSpawnDistance <= 0) throw new ArgumentException("幸存者 MinimumSpawnDistance 必须为有限正数");
            if (float.IsNaN(r.AimRange) || float.IsInfinity(r.AimRange) || r.AimRange <= 0) throw new ArgumentException("幸存者 AimRange 必须为有限正数");
            if (r.MinimumSpawnIntervalSeconds > r.SpawnIntervalSeconds || tables.TbArcadeRoundLayout.GetOrDefault(r.LayoutId) == null || tables.TbEnemy.GetOrDefault(r.EnemyId) == null || tables.TbArcadeWeapon.GetOrDefault(r.WeaponId) == null) throw new ArgumentException("幸存者引用或出生间隔无效");
            if (float.IsNaN(r.ExperienceDropChance) || float.IsInfinity(r.ExperienceDropChance) || r.ExperienceDropChance < 0 || r.ExperienceDropChance > 1)
                throw new ArgumentException("幸存者经验掉率必须在0到1之间");
            if (!global::Game.ResourceAddress.TryFromFileName(global::Game.ResourceAddressKind.SceneObjectPrefab,r.ExperienceVisualPrefabName,false,out var experienceAddress,out var experienceError))
                throw new ArgumentException("幸存者经验模型无效："+experienceError);
            var upgrades = tables.TbSurvivorUpgrade.DataList.Select(x => new SurvivorUpgradeDefinition(x.Id, x.Kind, x.AmountPerLevel, x.MaximumBonus, x.MaxLevel,x.Weight)).ToArray();
            if (upgrades.Select(x => x.Kind).Distinct().Count() != upgrades.Length || upgrades.Count(x => x.MaxLevel == 0) < 3) throw new ArgumentException("幸存者需要至少三种无限强化且类型不重复");
            if(r.WeaponSlots<1||r.WeaponSlots>3)throw new ArgumentException("幸存者武器槽必须为1至3");
            var weaponLevels=tables.TbSurvivorWeaponLevel.DataList.Select(x=>new SurvivorWeaponLevelDefinition(x.Kind,x.Level,x.Damage,x.FireIntervalSeconds,x.Range,x.ProjectileSpeed,x.ProjectileLifetimeSeconds)).ToArray();
            foreach(var kind in new[]{"Rifle","Drone"})
            {
                var levels=weaponLevels.Where(x=>x.Kind==kind).OrderBy(x=>x.Level).ToArray();
                if(levels.Length==0 || !levels.Select(x=>x.Level).SequenceEqual(Enumerable.Range(1,levels.Length)))throw new ArgumentException("武器等级必须从1连续配置且不重复");
                var upgrade=upgrades.SingleOrDefault(x=>x.Kind==kind);
                if(upgrade==null || upgrade.MaxLevel!=(kind=="Rifle"?levels.Length-1:levels.Length))throw new ArgumentException("武器升级上限与等级表不一致");
            }
            return new SurvivorConfiguration {
                Id = r.Id,
                WeaponSlots=r.WeaponSlots,
                RifleMagazineCapacity=r.RifleMagazineCapacity,
                RifleReloadSeconds=r.RifleReloadSeconds,
                RifleMinimumReloadSeconds=r.RifleMinimumReloadSeconds,
                WeaponLevels=Array.AsReadOnly(weaponLevels),
                LayoutId = r.LayoutId,
                EnemyId = r.EnemyId,
                WeaponId = r.WeaponId,
                PlayerMaxHealth = r.PlayerMaxHealth,
                PlayerMoveSpeed = r.PlayerMoveSpeed,
                SpawnIntervalSeconds = r.SpawnIntervalSeconds,
                MinimumSpawnIntervalSeconds = r.MinimumSpawnIntervalSeconds,
                SpawnIntervalReductionPerMinute = r.SpawnIntervalReductionPerMinute,
                EnemyHealthGrowthPerMinute = r.EnemyHealthGrowthPerMinute,
                EnemyDamageGrowthPerMinute = r.EnemyDamageGrowthPerMinute,
                EnemySpeedGrowthPerMinute = r.EnemySpeedGrowthPerMinute,
                MaxAliveEnemies = r.MaxAliveEnemies,
                MaxProjectiles = r.MaxProjectiles,
                MaxDrops = r.MaxDrops,
                InitialExperienceThreshold = r.InitialExperienceThreshold,
                ExperienceThresholdGrowth = r.ExperienceThresholdGrowth,
                ExperiencePerKill = r.ExperiencePerKill,
                ExperienceDropChance = r.ExperienceDropChance,
                ExperienceVisualPrefabName = r.ExperienceVisualPrefabName,
                ExperienceVisualPrefabAddress = experienceAddress,
                ScorePerKill = r.ScorePerKill,
                PickupRadius = r.PickupRadius,
                MinimumSpawnDistance = r.MinimumSpawnDistance,
                AimRange = r.AimRange,
                Upgrades = Array.AsReadOnly(upgrades)
            };
        }
    }
    /// <summary>强化只读定义；有限项线性增加，无限项采用渐近上限避免数值无限增长。</summary>
    public sealed class SurvivorUpgradeDefinition
    {
        /// <summary>强化配置 Id。</summary>
        public string Id { get; }
        /// <summary>强化配置 Kind。</summary>
        public string Kind { get; }
        /// <summary>强化配置 AmountPerLevel。</summary>
        public float AmountPerLevel { get; }
        /// <summary>强化配置 MaximumBonus。</summary>
        public float MaximumBonus { get; }
        /// <summary>强化配置 MaxLevel。</summary>
        public int MaxLevel { get; }
        /// <summary>候选无放回抽取权重。</summary>
        public int Weight { get; }
        /// <summary>武器候选使用独立装备等级，不把全局属性层数当武器等级。</summary>
        public bool IsWeapon => Kind=="Rifle"||Kind=="Drone";
        /// <summary>构造并拒绝未知类型及无效数值。</summary>
        public SurvivorUpgradeDefinition(string id, string kind, float amountPerLevel, float maximumBonus, int maxLevel)
            :this(id,kind,amountPerLevel,maximumBonus,maxLevel,10) { }
        /// <summary>正式配置显式传入权重；旧构造仅保留既有调用兼容。</summary>
        public SurvivorUpgradeDefinition(string id, string kind, float amountPerLevel, float maximumBonus, int maxLevel,int weight)
        {
            if (string.IsNullOrWhiteSpace(id) || !new[] { "Damage", "FireRate", "Health", "MoveSpeed", "Pickup","Rifle","Drone","Magazine","Reload" }.Contains(kind) || float.IsNaN(amountPerLevel) || float.IsInfinity(amountPerLevel) || amountPerLevel <= 0 || float.IsNaN(maximumBonus) || float.IsInfinity(maximumBonus) || maximumBonus <= 0 || maxLevel < 0 || weight<=0) throw new ArgumentException("幸存者强化配置无效");
            Id = id; Kind = kind; AmountPerLevel = amountPerLevel; MaximumBonus = maximumBonus; MaxLevel = maxLevel;Weight=weight;
        }
        /// <summary>返回累计加成；伤害/射速/移速为比例，生命/拾取为绝对值。</summary>
        public double BonusAtLevel(int level)
        {
            double count = Math.Max(0, level);
            // 弹夹按基础容量百分比逐层累加，不能套用无限属性的渐近算法。
            if(Kind=="Magazine")return Math.Min(MaximumBonus,count*AmountPerLevel);
            return MaxLevel == 0 ? MaximumBonus * count / (MaximumBonus / AmountPerLevel + count) : Math.Min(MaximumBonus, Math.Min(MaxLevel, count) * AmountPerLevel);
        }
    }

    /// <summary>幸存者武器一级完整参数快照；值只来自Luban权威表。</summary>
    public sealed class SurvivorWeaponLevelDefinition
    {
        /// <summary>武器类型及独立等级。</summary>
        public string Kind { get; }
        public int Level { get; }
        /// <summary>单发基础伤害与射击间隔。</summary>
        public int Damage { get; }
        public float FireIntervalSeconds { get; }
        /// <summary>瞄准距离、弹速及弹丸寿命。</summary>
        public float Range { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileLifetimeSeconds { get; }
        public SurvivorWeaponLevelDefinition(string kind,int level,int damage,float interval,float range,float speed,float lifetime)
        {
            if((kind!="Rifle"&&kind!="Drone")||level<1||damage<1||new[]{interval,range,speed,lifetime}.Any(x=>float.IsNaN(x)||float.IsInfinity(x)||x<=0))throw new ArgumentException("幸存者武器等级参数无效");
            Kind=kind;Level=level;Damage=damage;FireIntervalSeconds=interval;Range=range;ProjectileSpeed=speed;ProjectileLifetimeSeconds=lifetime;
        }
    }
}

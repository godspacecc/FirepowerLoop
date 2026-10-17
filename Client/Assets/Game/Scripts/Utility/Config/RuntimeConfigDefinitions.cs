using UnityEngine;

namespace Game
{
    /// <summary>街机共用物品及表现资源的只读配置快照。</summary>
    public readonly struct ItemDefinition
    {
        public ItemDefinition(
            int id,
            string name,
            string desc,
            string nameKey,
            string descKey,
            string iconAddress,
            global::ItemType itemType,
            int maxStackAmount,
            global::ItemUseEffect useEffect,
            int useValue,
            float useDurationSeconds = 0f,
            global::ItemActionKind actionKind = global::ItemActionKind.None,
            string actionTarget = "",
            global::ClothingSlot wearSlot = global::ClothingSlot.None,
            string appearanceId = "", float burnSeconds = 0f)
        {
            Id = id;
            Name = name;
            Desc = desc;
            NameKey = nameKey;
            DescKey = descKey;
            IconAddress = iconAddress;
            ItemType = itemType;
            MaxStackAmount = maxStackAmount;
            UseEffect = useEffect;
            UseValue = useValue;
            UseDurationSeconds = useDurationSeconds;
            ActionKind = actionKind;
            ActionTarget = actionTarget ?? string.Empty;
            WearSlot = wearSlot;
            AppearanceId = appearanceId ?? string.Empty;
            BurnSeconds = burnSeconds;
        }

        public int Id { get; }
        public string Name { get; }
        public string Desc { get; }
        public string NameKey { get; }
        public string DescKey { get; }
        public string IconAddress { get; }
        public global::ItemType ItemType { get; }
        public int MaxStackAmount { get; }
        public global::ItemUseEffect UseEffect { get; }
        public int UseValue { get; }
        public float UseDurationSeconds { get; }
        public global::ItemActionKind ActionKind { get; }
        public string ActionTarget { get; }
        /// <summary>身体穿戴位置；None 表示不可穿戴，与手持武器类型无关。</summary>
        public global::ClothingSlot WearSlot { get; }
        /// <summary>同骨架部件的稳定身份，不是运行时对象或资源句柄。</summary>
        public string AppearanceId { get; }
        /// <summary>火把每实例的初始燃烧秒数；0 表示非燃烧工具。</summary>
        public float BurnSeconds { get; }
    }

    /// <summary>武器规则的只读配置快照；提供攻击和装填参数，不保存装备、弹匣或背包状态。</summary>
    public readonly struct WeaponConfigDefinition
    {
        public WeaponConfigDefinition(
            int itemId,
            global::WeaponKind weaponKind,
            int attackDamage,
            float attackCooldownSeconds,
            float attackRange,
            float meleeArcDegrees,
            string projectileConfigId,
            int ammoItemId,
            int magazineCapacity = 0,
            float reloadSeconds = 0f,
            bool reloadWholeMagazine = false,
            float treeHarvestEfficiency = 0f,
            float stoneHarvestEfficiency = 0f)
        {
            ItemId = itemId;
            WeaponKind = weaponKind;
            AttackDamage = attackDamage;
            AttackCooldownSeconds = attackCooldownSeconds;
            AttackRange = attackRange;
            MeleeArcDegrees = meleeArcDegrees;
            ProjectileConfigId = projectileConfigId;
            AmmoItemId = ammoItemId;
            MagazineCapacity = magazineCapacity;
            ReloadSeconds = reloadSeconds;
            ReloadWholeMagazine = reloadWholeMagazine;
            TreeHarvestEfficiency = treeHarvestEfficiency;
            StoneHarvestEfficiency = stoneHarvestEfficiency;
        }

        public int ItemId { get; }
        public global::WeaponKind WeaponKind { get; }
        public int AttackDamage { get; }
        public float AttackCooldownSeconds { get; }
        public float AttackRange { get; }
        public float MeleeArcDegrees { get; }
        public string ProjectileConfigId { get; }
        public int AmmoItemId { get; }
        public int MagazineCapacity { get; }
        public float ReloadSeconds { get; }
        /// <summary>一次装填补足弹匣；false 每次只装一发，弹药不足时只装实际可用数量。</summary>
        public bool ReloadWholeMagazine { get; }
        /// <summary>近战砍树进度倍率，无单位；1 为标准效率，0 表示此武器不参与砍树。</summary>
        public float TreeHarvestEfficiency { get; }
        /// <summary>近战挖矿进度倍率，无单位；独立于战斗伤害，0 表示此武器不参与挖矿。</summary>
        public float StoneHarvestEfficiency { get; }
    }

    public readonly struct PlayerWeaponAppearanceDefinition
    {
        public PlayerWeaponAppearanceDefinition(
            int itemId,
            string prefabAddress,
            global::PlayerWeaponAttachPoint attachPoint,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            ItemId = itemId;
            PrefabAddress = prefabAddress;
            AttachPoint = attachPoint;
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
            LocalScale = localScale;
        }

        public int ItemId { get; }
        public string PrefabAddress { get; }
        public global::PlayerWeaponAttachPoint AttachPoint { get; }
        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
        public Vector3 LocalScale { get; }
    }

    public readonly struct ProjectileConfigDefinition
    {
        public ProjectileConfigDefinition(
            string id,
            string prefabAddress,
            float moveSpeed,
            float maxTravelDistance,
            string recoveryLootConfigId = "")
        {
            Id = id;
            PrefabAddress = prefabAddress;
            MoveSpeed = moveSpeed;
            MaxTravelDistance = maxTravelDistance;
            RecoveryLootConfigId = recoveryLootConfigId ?? string.Empty;
        }

        public string Id { get; }
        public string PrefabAddress { get; }
        public float MoveSpeed { get; }
        public float MaxTravelDistance { get; }
        /// <summary>撞障碍或飞满射程后的回收掉落配置；空值表示不回收，不影响正常弹药掉落。</summary>
        public string RecoveryLootConfigId { get; }
    }

    public readonly struct MapAssetDefinition
    {
        public MapAssetDefinition(string id, string sceneAddress)
            : this(id, new[] { sceneAddress })
        {
        }

        public MapAssetDefinition(string id, System.Collections.Generic.IEnumerable<string> sceneAddresses)
        {
            if (sceneAddresses == null) throw new System.ArgumentNullException(nameof(sceneAddresses));
            var copy = System.Linq.Enumerable.ToArray(sceneAddresses);
            if (copy.Length == 0 || System.Array.Exists(copy, string.IsNullOrWhiteSpace))
                throw new System.ArgumentException("场景地址集合不能为空且不得包含空白地址", nameof(sceneAddresses));

            Id = id;
            SceneAddresses = System.Array.AsReadOnly(copy);
        }

        public string Id { get; }
        public string SceneAddress => SceneAddresses[0];
        public System.Collections.Generic.IReadOnlyList<string> SceneAddresses { get; }
    }

    public readonly struct MapDisplayDefinition
    {
        /// <summary>
        /// 定义一张地图图片覆盖的世界边界及其可视分区；分区只影响展示缩放，不改变地图或场景身份。
        /// </summary>
        public MapDisplayDefinition(string mapId, string imageAddress, float centerX, float centerZ, float width, float height,
            int viewportColumns = 1, int viewportRows = 1)
        {
            MapId = mapId;
            ImageAddress = imageAddress;
            CenterX = centerX;
            CenterZ = centerZ;
            Width = width;
            Height = height;
            ViewportColumns = viewportColumns;
            ViewportRows = viewportRows;
        }

        public string MapId { get; }
        public string ImageAddress { get; }
        public float CenterX { get; }
        public float CenterZ { get; }
        public float Width { get; }
        public float Height { get; }
        /// <summary>地图图片横向可视分区数，必须大于零。</summary>
        public int ViewportColumns { get; }
        /// <summary>地图图片纵向可视分区数，必须大于零。</summary>
        public int ViewportRows { get; }
        public float MinX => CenterX - Width * 0.5f;
        public float MaxX => CenterX + Width * 0.5f;
        public float MinZ => CenterZ - Height * 0.5f;
        public float MaxZ => CenterZ + Height * 0.5f;
        public float ViewportAspectRatio => (Width / ViewportColumns) / (Height / ViewportRows);
    }

    public readonly struct MapSpawnDefinition
    {
        public MapSpawnDefinition(
            string id,
            string mapId,
            MapSpawnObjectType objectType,
            string configId,
            float positionX,
            float positionY,
            float positionZ,
            float rotationY)
        {
            Id = id;
            MapId = mapId;
            ObjectType = objectType;
            ConfigId = configId;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationY = rotationY;
            LocalPosition = new Vector3(positionX, positionY, positionZ);
            LocalRotation = Quaternion.Euler(0f, rotationY, 0f);
        }

        public string Id { get; }
        public string MapId { get; }
        public MapSpawnObjectType ObjectType { get; }
        public string ConfigId { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
        public float RotationY { get; }
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
    }

    public readonly struct PlayerConfigDefinition
    {
        public PlayerConfigDefinition(
            string id,
            string prefabAddress,
            int maxHP,
            int attackDamage,
            float attackRange,
            float attackCooldownSeconds,
            float moveSpeed,
            float turnSpeed,
            float baseRecoveryPerSecond)
            :this(id,prefabAddress,maxHP,attackDamage,attackRange,attackCooldownSeconds,moveSpeed,turnSpeed,baseRecoveryPerSecond,3,1.4f,0.6f,75f)
        { }

        public PlayerConfigDefinition(
            string id,string prefabAddress,int maxHP,int attackDamage,float attackRange,float attackCooldownSeconds,
            float moveSpeed,float turnSpeed,float baseRecoveryPerSecond,int unarmedAttackDamage,float unarmedAttackRange,
            float unarmedAttackCooldownSeconds,float unarmedMeleeArcDegrees,float buildingOperateDistance = 0f)
        {
            Id = id;
            PrefabAddress = prefabAddress;
            MaxHP = maxHP;
            AttackDamage = attackDamage;
            AttackRange = attackRange;
            AttackCooldownSeconds = attackCooldownSeconds;
            MoveSpeed = moveSpeed;
            TurnSpeed = turnSpeed;
            BaseRecoveryPerSecond = baseRecoveryPerSecond;
            UnarmedAttackDamage=unarmedAttackDamage;UnarmedAttackRange=unarmedAttackRange;UnarmedAttackCooldownSeconds=unarmedAttackCooldownSeconds;UnarmedMeleeArcDegrees=unarmedMeleeArcDegrees;
            BuildingOperateDistance = buildingOperateDistance;
        }

        public string Id { get; }
        public string PrefabAddress { get; }
        public int MaxHP { get; }
        public int AttackDamage { get; }
        public float AttackRange { get; }
        public float AttackCooldownSeconds { get; }
        public float MoveSpeed { get; }
        public float TurnSpeed { get; }
        public float BaseRecoveryPerSecond { get; }
        public int UnarmedAttackDamage { get; }
        public float UnarmedAttackRange { get; }
        public float UnarmedAttackCooldownSeconds { get; }
        public float UnarmedMeleeArcDegrees { get; }
        /// <summary>玩家到建筑占地边缘的操作距离，单位米；建造与拆除共用，0 表示未提供有效配置。</summary>
        public float BuildingOperateDistance { get; }
    }

    public readonly struct EnemyConfigDefinition
    {
        public EnemyConfigDefinition(
            string id,
            string prefabAddress,
            int maxHP,
            int attackDamage,
            float aggroRange,
            float attackRange,
            float attackCooldownSeconds,
            float moveSpeed,
            string lootConfigId)
            : this(
                id,
                prefabAddress,
                maxHP,
                attackDamage,
                aggroRange,
                attackRange,
                attackCooldownSeconds,
                moveSpeed,
                lootConfigId,
                global::EnemyAttackKind.Melee,
                0f,
                0f,
                string.Empty)
        {
        }

        public EnemyConfigDefinition(
            string id,
            string prefabAddress,
            int maxHP,
            int attackDamage,
            float aggroRange,
            float attackRange,
            float attackCooldownSeconds,
            float moveSpeed,
            string lootConfigId,
            global::EnemyAttackKind attackKind,
            float retreatDistance,
            float telegraphSeconds,
            string projectileConfigId)
            : this(
                id,
                prefabAddress,
                maxHP,
                attackDamage,
                aggroRange,
                attackRange,
                attackCooldownSeconds,
                moveSpeed,
                lootConfigId,
                attackKind,
                retreatDistance,
                telegraphSeconds,
                projectileConfigId,
                string.Empty,
                string.Empty,
                0,
                0f)
        {
        }

        public EnemyConfigDefinition(
            string id,
            string prefabAddress,
            int maxHP,
            int attackDamage,
            float aggroRange,
            float attackRange,
            float attackCooldownSeconds,
            float moveSpeed,
            string lootConfigId,
            global::EnemyAttackKind attackKind,
            float retreatDistance,
            float telegraphSeconds,
            string projectileConfigId,
            string guaranteedWeaponLootConfigId,
            string guaranteedAmmoLootConfigId,
            int ammoCapacity,
            float fallbackMeleeAttackRange,
            int magazineCapacity = 0,
            float reloadSeconds = 0f,
            string weaponId = null)
        {
            Id = id;
            PrefabAddress = prefabAddress;
            MaxHP = maxHP;
            AttackDamage = attackDamage;
            AggroRange = aggroRange;
            AttackRange = attackRange;
            AttackCooldownSeconds = attackCooldownSeconds;
            MoveSpeed = moveSpeed;
            LootConfigId = lootConfigId;
            AttackKind = attackKind;
            RetreatDistance = retreatDistance;
            TelegraphSeconds = telegraphSeconds;
            ProjectileConfigId = projectileConfigId;
            GuaranteedWeaponLootConfigId = guaranteedWeaponLootConfigId;
            GuaranteedAmmoLootConfigId = guaranteedAmmoLootConfigId;
            AmmoCapacity = ammoCapacity;
            FallbackMeleeAttackRange = fallbackMeleeAttackRange;
            MagazineCapacity = magazineCapacity;
            ReloadSeconds = reloadSeconds;
            WeaponId = weaponId ?? string.Empty;
        }

        public string Id { get; }
        public string PrefabAddress { get; }
        public int MaxHP { get; }
        public int AttackDamage { get; }
        public float AggroRange { get; }
        public float AttackRange { get; }
        public float AttackCooldownSeconds { get; }
        public float MoveSpeed { get; }
        public string LootConfigId { get; }
        public global::EnemyAttackKind AttackKind { get; }
        public float RetreatDistance { get; }
        public float TelegraphSeconds { get; }
        public string ProjectileConfigId { get; }
        public string GuaranteedWeaponLootConfigId { get; }
        public string GuaranteedAmmoLootConfigId { get; }
        public int AmmoCapacity { get; }
        public float FallbackMeleeAttackRange { get; }
        public int MagazineCapacity { get; }
        public float ReloadSeconds { get; }
        /// <summary>五角色共享武器目录的稳定身份；战斗数值仍由本敌人配置决定。</summary>
        public string WeaponId { get; }
    }

    public readonly struct LootConfigDefinition
    {
        public LootConfigDefinition(string id, string prefabAddress, int itemId, int amount)
        {
            Id = id;
            PrefabAddress = prefabAddress;
            ItemId = itemId;
            Amount = amount;
        }

        public string Id { get; }
        public string PrefabAddress { get; }
        public int ItemId { get; }
        public int Amount { get; }
    }

    public readonly struct TestCmdCategoryDefinition
    {
        public TestCmdCategoryDefinition(string id, int order, string labelText)
        {
            Id = id;
            Order = order;
            LabelText = labelText;
        }

        public string Id { get; }
        public int Order { get; }
        public string LabelText { get; }
    }

    public readonly struct TestCmdEntryDefinition
    {
        public TestCmdEntryDefinition(
            string id,
            string categoryId,
            int order,
            string labelText,
            string commandTemplate,
            string descriptionText,
            string usageText,
            string exampleText)
        {
            Id = id;
            CategoryId = categoryId;
            Order = order;
            LabelText = labelText;
            CommandTemplate = commandTemplate;
            DescriptionText = descriptionText;
            UsageText = usageText;
            ExampleText = exampleText;
        }

        public string Id { get; }
        public string CategoryId { get; }
        public int Order { get; }
        public string LabelText { get; }
        public string CommandTemplate { get; }
        public string DescriptionText { get; }
        public string UsageText { get; }
        public string ExampleText { get; }
    }

    /// <summary>街机模式全局开局、难度增长和移动端预算配置。</summary>
    public readonly struct ArcadeRuleDefinition
    {
        public ArcadeRuleDefinition(string id, int initialLives, int initialBombs, int initialDashes, string baseWeaponId,
            int maxAliveEnemies, int maxProjectiles, int maxDrops, float healthGrowthPerArena,
            float damageGrowthPerArena, float speedGrowthPerArena, float goldGrowthPerArena, int bossEveryArenas,
            int playerMaxHealth, float playerMoveSpeed, float playerRadius, float enemyContactRadius,
            float contactInvulnerabilitySeconds, float respawnDelaySeconds, float arenaHalfWidth, float arenaHalfHeight,
            float dropChance, float supportFireIntervalSeconds, string configFingerprint = "",
            float coopDownedSeconds=0, float coopRescueRadius=0, float coopRescueSpeed=0, int coopDonationMinLives=0)
        {
            Id = id; InitialLives = initialLives; InitialBombs = initialBombs; InitialDashes = initialDashes;
            BaseWeaponId = baseWeaponId; MaxAliveEnemies = maxAliveEnemies;
            MaxProjectiles = maxProjectiles; MaxDrops = maxDrops; HealthGrowthPerArena = healthGrowthPerArena;
            DamageGrowthPerArena = damageGrowthPerArena; SpeedGrowthPerArena = speedGrowthPerArena;
            GoldGrowthPerArena = goldGrowthPerArena; BossEveryArenas = bossEveryArenas;
            PlayerMaxHealth = playerMaxHealth; PlayerMoveSpeed = playerMoveSpeed; PlayerRadius = playerRadius;
            EnemyContactRadius = enemyContactRadius; ContactInvulnerabilitySeconds = contactInvulnerabilitySeconds;
            RespawnDelaySeconds = respawnDelaySeconds; ArenaHalfWidth = arenaHalfWidth; ArenaHalfHeight = arenaHalfHeight;
            DropChance = dropChance; SupportFireIntervalSeconds = supportFireIntervalSeconds;
            ConfigFingerprint = configFingerprint ?? string.Empty;
            CoopDownedSeconds=coopDownedSeconds;CoopRescueRadius=coopRescueRadius;CoopRescueSpeed=coopRescueSpeed;CoopDonationMinLives=coopDonationMinLives;
        }

        public string Id { get; }
        /// <summary>双端兼容身份；调整任何影响联机玩法的街机配置时必须同步更新。</summary>
        public string ConfigFingerprint { get; }
        public int InitialLives { get; }
        public int InitialBombs { get; }
        public int InitialDashes { get; }
        public string BaseWeaponId { get; }
        public int MaxAliveEnemies { get; }
        public int MaxProjectiles { get; }
        public int MaxDrops { get; }
        public float HealthGrowthPerArena { get; }
        public float DamageGrowthPerArena { get; }
        public float SpeedGrowthPerArena { get; }
        public float GoldGrowthPerArena { get; }
        public int BossEveryArenas { get; }
        public int PlayerMaxHealth { get; }
        public float PlayerMoveSpeed { get; }
        public float PlayerRadius { get; }
        public float EnemyContactRadius { get; }
        public float ContactInvulnerabilitySeconds { get; }
        public float RespawnDelaySeconds { get; }
        public float ArenaHalfWidth { get; }
        public float ArenaHalfHeight { get; }
        public float DropChance { get; }
        public float SupportFireIntervalSeconds { get; }
        /// <summary>合作倒地与救援规则，由双端 Config 提供。</summary>
        public float CoopDownedSeconds { get; }
        public float CoopRescueRadius { get; }
        public float CoopRescueSpeed { get; }
        public int CoopDonationMinLives { get; }
    }

    /// <summary>街机武器的近战或投射规则及表现参数。</summary>
    public readonly struct ArcadeWeaponDefinition
    {
        public ArcadeWeaponDefinition(string id, string heldVisualResourceName, string projectileVisualResourceName,
            bool muzzleFlash, Vector3 projectileFirePointLocalPosition,
            int damage, float fireIntervalSeconds, float projectileSpeed,
            float projectileLifetimeSeconds, int projectileCount, float spreadDegrees,
            global::ArcadeWeaponBehavior behavior, int maxHits, float explosionRadius,
            float meleeRange = 0f, float meleeArcDegrees = 0f)
        {
            Id = id;
            HeldVisualResourceName = heldVisualResourceName ?? string.Empty;
            ProjectileVisualResourceName = projectileVisualResourceName ?? string.Empty;
            MuzzleFlash = muzzleFlash;
            ProjectileFirePointLocalPosition = projectileFirePointLocalPosition;
            Damage = damage; FireIntervalSeconds = fireIntervalSeconds; ProjectileSpeed = projectileSpeed;
            ProjectileLifetimeSeconds = projectileLifetimeSeconds; ProjectileCount = projectileCount;
            SpreadDegrees = spreadDegrees; Behavior = behavior; MaxHits = maxHits; ExplosionRadius = explosionRadius;
            MeleeRange = meleeRange; MeleeArcDegrees = meleeArcDegrees;
        }
        public string Id { get; }
        /// <summary>玩家 Prefab 武器注册表中的模型资源名；空值表示隐藏专用手持模型。</summary>
        public string HeldVisualResourceName { get; }
        /// <summary>投射物模型资源名；空值使用街机短子弹，GrayboxArrow 保留弓箭，特殊模型沿用原载体尺寸。</summary>
        public string ProjectileVisualResourceName { get; }
        public bool MuzzleFlash { get; }
        /// <summary>表现发射点相对当前武器模型根节点的位置，单位为米；不参与权威弹道。</summary>
        public Vector3 ProjectileFirePointLocalPosition { get; }
        public int Damage { get; }
        public float FireIntervalSeconds { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileLifetimeSeconds { get; }
        public int ProjectileCount { get; }
        public float SpreadDegrees { get; }
        /// <summary>决定弹丸是单体、贯穿还是命中后范围爆炸。</summary>
        public global::ArcadeWeaponBehavior Behavior { get; }
        /// <summary>单枚弹丸最多命中的不同敌人数；爆炸武器固定为 1。</summary>
        public int MaxHits { get; }
        /// <summary>爆炸伤害半径，单位为竞技场米；非爆炸武器必须为 0。</summary>
        public float ExplosionRadius { get; }
        /// <summary>近战命中距离，单位为竞技场米；非近战武器为 0。</summary>
        public float MeleeRange { get; }
        /// <summary>近战前方扇形的总角度，单位为度；非近战武器为 0。</summary>
        public float MeleeArcDegrees { get; }
    }

    public readonly struct ArcadeEnemyMemberDefinition
    {
        public ArcadeEnemyMemberDefinition(string enemyId, int count) { EnemyId = enemyId; Count = count; }
        public string EnemyId { get; }
        public int Count { get; }
    }

    public sealed class ArcadeEnemyGroupDefinition
    {
        public ArcadeEnemyGroupDefinition(string id, System.Collections.Generic.IEnumerable<ArcadeEnemyMemberDefinition> members,
            float spawnIntervalSeconds, int maxAlive, float healthMultiplier, float damageMultiplier, float speedMultiplier, int clearGold)
        {
            Id = id;
            Members = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(members ?? System.Array.Empty<ArcadeEnemyMemberDefinition>()));
            SpawnIntervalSeconds = spawnIntervalSeconds; MaxAlive = maxAlive; HealthMultiplier = healthMultiplier;
            DamageMultiplier = damageMultiplier; SpeedMultiplier = speedMultiplier; ClearGold = clearGold;
        }

        public string Id { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeEnemyMemberDefinition> Members { get; }
        public float SpawnIntervalSeconds { get; }
        public int MaxAlive { get; }
        public float HealthMultiplier { get; }
        public float DamageMultiplier { get; }
        public float SpeedMultiplier { get; }
        public int ClearGold { get; }
    }

    public readonly struct ArcadeObstacleDefinition
    {
        public ArcadeObstacleDefinition(float centerX, float centerY, float halfWidth, float halfHeight)
        {
            Center = new Vector2(centerX, centerY);
            HalfExtents = new Vector2(halfWidth, halfHeight);
        }

        /// <summary>以竞技场中心为原点的局部坐标。</summary>
        public Vector2 Center { get; }
        /// <summary>轴对齐矩形的半宽与半高，单位为米。</summary>
        public Vector2 HalfExtents { get; }
    }

    public sealed class ArcadeArenaDefinition
    {
        public ArcadeArenaDefinition(int arenaNumber, System.Collections.Generic.IEnumerable<string> waveGroupIds,
            System.Collections.Generic.IEnumerable<ArcadeObstacleDefinition> obstacles,
            bool isBoss, string bossId, int clearGold, int encounterSeedSalt,
            System.Collections.Generic.IEnumerable<FirepowerLoop.Arcade.ArcadeHazardParameters> hazards = null,
            System.Collections.Generic.IEnumerable<int> nextArenaNumbers = null, string finalBossId = null,
            FirepowerLoop.Arcade.ArcadeOpeningParameters opening = default,
            FirepowerLoop.Arcade.ArcadeBabylonParameters babylon = null)
        {
            ArenaNumber = arenaNumber;
            WaveGroupIds = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(waveGroupIds ?? System.Array.Empty<string>()));
            Obstacles = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(obstacles ?? System.Array.Empty<ArcadeObstacleDefinition>()));
            IsBoss = isBoss; BossId = bossId ?? string.Empty; ClearGold = clearGold; EncounterSeedSalt = encounterSeedSalt;
            Hazards = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(hazards ?? System.Array.Empty<FirepowerLoop.Arcade.ArcadeHazardParameters>()));
            NextArenaNumbers = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(nextArenaNumbers ?? System.Array.Empty<int>()));
            FinalBossId = finalBossId ?? string.Empty;
            Opening = opening;
            Babylon = babylon;
        }

        public int ArenaNumber { get; }
        /// <summary>首场水火与休整合同；禁用值表示沿用其他场地既有流程。</summary>
        public FirepowerLoop.Arcade.ArcadeOpeningParameters Opening { get; }
        public FirepowerLoop.Arcade.ArcadeBabylonParameters Babylon { get; }
        public System.Collections.Generic.IReadOnlyList<string> WaveGroupIds { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition> Obstacles { get; }
        /// <summary>独立场地机关目录；只描述数值，碰撞伤害由单人或服务端权威运行态裁决。</summary>
        public System.Collections.Generic.IReadOnlyList<FirepowerLoop.Arcade.ArcadeHazardParameters> Hazards { get; }
        public bool IsBoss { get; }
        /// <summary>非空时，最后一轮唯一敌人使用该专用 Boss 行为；空值仍保留旧倍率占位规则。</summary>
        public string BossId { get; }
        public int ClearGold { get; }
        public int EncounterSeedSalt { get; }
        /// <summary>清场后实体出口的稳定目的地配置编号；空列表表示主线末场。</summary>
        public System.Collections.Generic.IReadOnlyList<int> NextArenaNumbers { get; }
        /// <summary>四轮主线之后独立战斗的 Boss；不占用任何主线轮次。</summary>
        public string FinalBossId { get; }
    }

    /// <summary>单人与服务器共享的 Boss 冲锋、射线和增援参数；不持有战斗状态。</summary>
    public readonly struct ArcadeBossDefinition
    {
        public ArcadeBossDefinition(string id, string enemyGroupId, float skillCooldownSeconds,
            float telegraphSeconds, float chargeDurationSeconds, float chargeSpeedMultiplier,
            float impactRadius, float impactDamageMultiplier, float rayIntervalSeconds = 0f,
            float rayTelegraphSeconds = 0f, float raySpeed = 0f, float rayDamageMultiplier = 0f,
            string reinforcementGroupId = null, float reinforcementIntervalSeconds = 0f)
        {
            Id = id; EnemyGroupId = enemyGroupId; SkillCooldownSeconds = skillCooldownSeconds;
            TelegraphSeconds = telegraphSeconds; ChargeDurationSeconds = chargeDurationSeconds;
            ChargeSpeedMultiplier = chargeSpeedMultiplier; ImpactRadius = impactRadius;
            ImpactDamageMultiplier = impactDamageMultiplier;
            RayIntervalSeconds = rayIntervalSeconds; RayTelegraphSeconds = rayTelegraphSeconds;
            RaySpeed = raySpeed; RayDamageMultiplier = rayDamageMultiplier;
            ReinforcementGroupId = reinforcementGroupId ?? string.Empty;
            ReinforcementIntervalSeconds = reinforcementIntervalSeconds;
        }

        public string Id { get; }
        public string EnemyGroupId { get; }
        public float SkillCooldownSeconds { get; }
        public float TelegraphSeconds { get; }
        public float ChargeDurationSeconds { get; }
        public float ChargeSpeedMultiplier { get; }
        public float ImpactRadius { get; }
        public float ImpactDamageMultiplier { get; }
        /// <summary>射线冷却秒数；零表示该 Boss 不具备射线攻击。</summary>
        public float RayIntervalSeconds { get; }
        public float RayTelegraphSeconds { get; }
        public float RaySpeed { get; }
        public float RayDamageMultiplier { get; }
        /// <summary>独立 Boss 战期间反复补充的普通敌群；空值不产生增援。</summary>
        public string ReinforcementGroupId { get; }
        public float ReinforcementIntervalSeconds { get; }
    }

    /// <summary>特殊挑战的共享规则；只定义开放节奏、敌群和倒计时，不保存尝试状态。</summary>
    public readonly struct ArcadeChallengeDefinition
    {
        public ArcadeChallengeDefinition(string id, string enemyGroupId, int startArena, int interval,
            float timeLimitSeconds)
        {
            Id = id; EnemyGroupId = enemyGroupId; StartArena = startArena; Interval = interval;
            TimeLimitSeconds = timeLimitSeconds;
        }

        public string Id { get; }
        public string EnemyGroupId { get; }
        public int StartArena { get; }
        public int Interval { get; }
        public float TimeLimitSeconds { get; }
    }

    /// <summary>街机掉落规则；产品身份与可选地面美术 Prefab 地址均由配置提供。</summary>
    public readonly struct ArcadeDropDefinition
    {
        public ArcadeDropDefinition(string id, int minArena, int maxArena, int weight, global::ArcadeDropKind kind,
            string productId, string visualPrefabAddress, int amount, float durationSeconds, int maxOwned)
        {
            Id = id; MinArena = minArena; MaxArena = maxArena; Weight = weight; Kind = kind;
            ProductId = productId; VisualPrefabAddress = visualPrefabAddress ?? string.Empty;
            Amount = amount; DurationSeconds = durationSeconds; MaxOwned = maxOwned;
        }
        public string Id { get; }
        public int MinArena { get; }
        public int MaxArena { get; }
        public int Weight { get; }
        public global::ArcadeDropKind Kind { get; }
        public string ProductId { get; }
        /// <summary>可直接加载的地面美术 Prefab 地址；空值表示使用通用方块。</summary>
        public string VisualPrefabAddress { get; }
        public int Amount { get; }
        public float DurationSeconds { get; }
        public int MaxOwned { get; }
    }

    /// <summary>战斗强化道具的只读规则；同时提供可替换的生效视觉，不持有 Unity 实例。</summary>
    public readonly struct ArcadePowerupDefinition
    {
        public ArcadePowerupDefinition(string id, global::ArcadePowerupBehavior behavior,
            string activeVisualPrefabAddress, bool visualFollowPlayer, Vector3 visualOffset, float magnitude,
            int damage, float intervalSeconds, float radius, float projectileSpeed, int charges)
        {
            Id = id; Behavior = behavior; ActiveVisualPrefabAddress = activeVisualPrefabAddress ?? string.Empty;
            VisualFollowPlayer = visualFollowPlayer; VisualOffset = visualOffset; Magnitude = magnitude; Damage = damage;
            IntervalSeconds = intervalSeconds; Radius = radius; ProjectileSpeed = projectileSpeed; Charges = charges;
        }
        public string Id { get; }
        public global::ArcadePowerupBehavior Behavior { get; }
        /// <summary>强化生效期间加载的纯视觉 Prefab 地址。</summary>
        public string ActiveVisualPrefabAddress { get; }
        /// <summary>为 true 时视觉锚定玩家，否则锚定首次拾取位置。</summary>
        public bool VisualFollowPlayer { get; }
        /// <summary>相对竞技场锚点的本地偏移，单位米。</summary>
        public Vector3 VisualOffset { get; }
        public float Magnitude { get; }
        public int Damage { get; }
        public float IntervalSeconds { get; }
        public float Radius { get; }
        public float ProjectileSpeed { get; }
        public int Charges { get; }
    }

    /// <summary>旧局已持有支援的表现定义，不提供价格、商品或发放能力。</summary>
    public readonly struct ArcadeSupportVisualDefinition
    {
        /// <summary>从正式配置映射旧局已持有支援的表现，不创建任何装备。</summary>
        public ArcadeSupportVisualDefinition(string id, string productId, string equipmentSlotId,
            string activeVisualPrefabAddress, Vector3 visualOffset)
        { Id = id; ProductId = productId; EquipmentSlotId = equipmentSlotId; ActiveVisualPrefabAddress = activeVisualPrefabAddress; VisualOffset = visualOffset; }
        /// <summary>支援表现配置身份。</summary>
        public string Id { get; }
        /// <summary>旧局装备身份，保持原值以恢复已有支援。</summary>
        public string ProductId { get; }
        /// <summary>所属支援装备槽。</summary>
        public string EquipmentSlotId { get; }
        /// <summary>纯表现资源地址，由视图拥有加载句柄。</summary>
        public string ActiveVisualPrefabAddress { get; }
        /// <summary>相对角色的本地表现偏移。</summary>
        public Vector3 VisualOffset { get; }
    }

    public readonly struct ArcadeUpgradeDefinition
    {
        public ArcadeUpgradeDefinition(string id, global::ArcadeUpgradeKind kind, float valuePerStack, int maxStacks)
        { Id = id; Kind = kind; ValuePerStack = valuePerStack; MaxStacks = maxStacks; }
        public string Id { get; }
        public global::ArcadeUpgradeKind Kind { get; }
        public float ValuePerStack { get; }
        public int MaxStacks { get; }
    }

    public readonly struct ArcadeRewardDefinition
    {
        public ArcadeRewardDefinition(string id, global::ArcadeRewardTrigger trigger, int startArena, int interval,
            string rewardId, int amount, int amountPerTrigger)
        { Id = id; Trigger = trigger; StartArena = startArena; Interval = interval; RewardId = rewardId; Amount = amount; AmountPerTrigger = amountPerTrigger; }
        public string Id { get; }
        public global::ArcadeRewardTrigger Trigger { get; }
        public int StartArena { get; }
        public int Interval { get; }
        public string RewardId { get; }
        public int Amount { get; }
        public int AmountPerTrigger { get; }
    }

    /// <summary>整表校验完成后的不可变街机配置快照，供规则和战斗模块读取。</summary>
    public sealed class ArcadeConfigCatalogDefinition
    {
        public ArcadeConfigCatalogDefinition(ArcadeRuleDefinition rule,
            System.Collections.Generic.IEnumerable<ArcadeWeaponDefinition> weapons,
            System.Collections.Generic.IEnumerable<ArcadeArenaDefinition> arenas,
            System.Collections.Generic.IEnumerable<ArcadeEnemyGroupDefinition> enemyGroups,
            System.Collections.Generic.IEnumerable<ArcadeBossDefinition> bosses,
            System.Collections.Generic.IEnumerable<ArcadeChallengeDefinition> challenges,
            System.Collections.Generic.IEnumerable<ArcadeDropDefinition> drops,
            System.Collections.Generic.IEnumerable<ArcadeSupportVisualDefinition> supportVisuals,
            System.Collections.Generic.IEnumerable<ArcadeUpgradeDefinition> upgrades,
            System.Collections.Generic.IEnumerable<ArcadeRewardDefinition> rewards,
            System.Collections.Generic.IEnumerable<FirepowerLoop.Arcade.ArcadeTreasureRoomParameters> treasureRooms = null,
            System.Collections.Generic.IEnumerable<FirepowerLoop.Arcade.ArcadeRoundLayoutParameters> roundLayouts = null)
            : this(rule, weapons, arenas, enemyGroups, bosses, challenges, drops,
                System.Array.Empty<ArcadePowerupDefinition>(), supportVisuals, upgrades, rewards, treasureRooms, roundLayouts)
        {
        }

        public ArcadeConfigCatalogDefinition(ArcadeRuleDefinition rule,
            System.Collections.Generic.IEnumerable<ArcadeWeaponDefinition> weapons,
            System.Collections.Generic.IEnumerable<ArcadeArenaDefinition> arenas,
            System.Collections.Generic.IEnumerable<ArcadeEnemyGroupDefinition> enemyGroups,
            System.Collections.Generic.IEnumerable<ArcadeBossDefinition> bosses,
            System.Collections.Generic.IEnumerable<ArcadeChallengeDefinition> challenges,
            System.Collections.Generic.IEnumerable<ArcadeDropDefinition> drops,
            System.Collections.Generic.IEnumerable<ArcadePowerupDefinition> powerups,
            System.Collections.Generic.IEnumerable<ArcadeSupportVisualDefinition> supportVisuals,
            System.Collections.Generic.IEnumerable<ArcadeUpgradeDefinition> upgrades,
            System.Collections.Generic.IEnumerable<ArcadeRewardDefinition> rewards,
            System.Collections.Generic.IEnumerable<FirepowerLoop.Arcade.ArcadeTreasureRoomParameters> treasureRooms = null,
            System.Collections.Generic.IEnumerable<FirepowerLoop.Arcade.ArcadeRoundLayoutParameters> roundLayouts = null)
        {
            Rule = rule;
            Weapons = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(weapons));
            Arenas = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(arenas));
            EnemyGroups = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(enemyGroups));
            Bosses = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(bosses));
            Challenges = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(challenges));
            Drops = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(drops));
            Powerups = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(powerups));
            SupportVisuals = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(supportVisuals));
            Upgrades = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(upgrades));
            Rewards = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(rewards));
            TreasureRooms = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(treasureRooms ?? System.Array.Empty<FirepowerLoop.Arcade.ArcadeTreasureRoomParameters>()));
            RoundLayouts = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(roundLayouts ?? System.Array.Empty<FirepowerLoop.Arcade.ArcadeRoundLayoutParameters>()));
        }

        public ArcadeRuleDefinition Rule { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeWeaponDefinition> Weapons { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeArenaDefinition> Arenas { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeEnemyGroupDefinition> EnemyGroups { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeBossDefinition> Bosses { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeChallengeDefinition> Challenges { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeDropDefinition> Drops { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadePowerupDefinition> Powerups { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeSupportVisualDefinition> SupportVisuals { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeUpgradeDefinition> Upgrades { get; }
        public System.Collections.Generic.IReadOnlyList<ArcadeRewardDefinition> Rewards { get; }
        public System.Collections.Generic.IReadOnlyList<FirepowerLoop.Arcade.ArcadeTreasureRoomParameters> TreasureRooms { get; }
        /// <summary>全部主题的不可变轮级地图目录；旧调用可省略新目录参数。</summary>
        public System.Collections.Generic.IReadOnlyList<FirepowerLoop.Arcade.ArcadeRoundLayoutParameters> RoundLayouts { get; }
    }
}

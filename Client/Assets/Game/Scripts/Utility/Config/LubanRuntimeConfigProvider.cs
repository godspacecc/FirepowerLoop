using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>校验并转换 Luban 配置为运行时只读定义；仅提供街机和共用表现所需只读配置。</summary>
    public sealed class LubanRuntimeConfigProvider : IRuntimeConfigProvider
    {
        /// <summary>正式Luban源必须提供商店门禁；损坏配置不能伪装成旧规则开局。</summary>
        public bool HasSlayerMartConfiguration => true;

        private readonly Func<global::Tables> tablesFactory;
        private global::Tables cachedTables;
        private bool validated;
        private string validationError;
        private IReadOnlyList<TestCmdCategoryDefinition> testCmdCategories;
        private IReadOnlyList<TestCmdEntryDefinition> testCmdEntries;
        private IReadOnlyDictionary<int, LootConfigDefinition> lootByItemId;
        private IReadOnlyList<ProjectileConfigDefinition> projectiles;
        private IReadOnlyDictionary<int, PlayerWeaponAppearanceDefinition> playerWeaponAppearanceByItemId;
        private ArcadeConfigCatalogDefinition arcadeCatalog;
        /// <summary>正式商店配置独立于旧街机目录，开放门禁由配置明确控制。</summary>
        private FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration slayerMart;
        private FirepowerLoop.Arcade.ArcadeSlayerChallengeConfiguration slayerChallenge;

        public LubanRuntimeConfigProvider()
            : this(() => DataLoader.Tables)
        {
        }

        public LubanRuntimeConfigProvider(Func<global::Tables> tablesFactory)
        {
            this.tablesFactory = tablesFactory ?? throw new ArgumentNullException(nameof(tablesFactory));
        }

        public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbMap.GetOrDefault(id);
            if (row == null)
            {
                error = $"TbMap[{id}].Id 不存在";
                return false;
            }

            var addresses = new List<string>
            {
                ResolveRelativePath(ResourceAddressKind.MapScene, row.ScenePath)
            };
            addresses.AddRange(Tables.TbMapScenePart.DataList
                .Where(part => string.Equals(part.MapId, id, StringComparison.Ordinal))
                .OrderBy(part => part.Order)
                .Select(part => ResolveRelativePath(ResourceAddressKind.MapScene, part.ScenePath)));
            value = new MapAssetDefinition(row.Id, addresses);
            return true;
        }

        public bool TryGetItem(int id, out ItemDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbItem.GetOrDefault(id);
            if (row == null)
            {
                error = $"TbItem[{id}].Id 不存在";
                return false;
            }

            value = new ItemDefinition(
                row.Id,
                row.Name,
                row.Desc,
                row.NameKey,
                row.DescKey,
                ResolveFileName(ResourceAddressKind.ItemIcon, row.IconName, true),
                row.ItemType,
                row.MaxStackAmount,
                row.UseEffect,
                row.UseValue,
                row.UseDurationSeconds,
                row.ActionKind,
                row.ActionTarget, row.WearSlot, row.AppearanceId, row.BurnSeconds);
            return true;
        }

        public bool TryGetItems(out IReadOnlyList<ItemDefinition> values, out string error)
        {
            values = Array.Empty<ItemDefinition>();
            if (!EnsureValid(out error))
            {
                return false;
            }

            values = Tables.TbItem.DataList
                .Select(row => new ItemDefinition(
                    row.Id,
                    row.Name,
                    row.Desc,
                    row.NameKey,
                    row.DescKey,
                    ResolveFileName(ResourceAddressKind.ItemIcon, row.IconName, true),
                    row.ItemType,
                    row.MaxStackAmount,
                    row.UseEffect,
                    row.UseValue,
                    row.UseDurationSeconds,
                    row.ActionKind,
                    row.ActionTarget, row.WearSlot, row.AppearanceId, row.BurnSeconds))
                .ToArray();
            return true;
        }

        public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbWeapon.GetOrDefault(itemId);
            if (row == null)
            {
                error = $"TbWeapon[{itemId}].ItemId 不存在";
                return false;
            }

            value = ToDefinition(row);
            return true;
        }

        public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbProjectile.GetOrDefault(id);
            if (row == null)
            {
                error = $"TbProjectile[{id}].Id 不存在";
                return false;
            }

            value = ToDefinition(row);
            return true;
        }

        /// <summary>返回首次整表校验后按顺序冻结的任务快照；已知无行地图是成功空集合。</summary>
        public bool TryGetProjectiles(out IReadOnlyList<ProjectileConfigDefinition> values, out string error)
        {
            values = Array.Empty<ProjectileConfigDefinition>();
            if (!EnsureValid(out error))
            {
                return false;
            }

            values = projectiles;
            error = null;
            return true;
        }

        public bool TryGetPlayerWeaponAppearance(int itemId, out PlayerWeaponAppearanceDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            if (!playerWeaponAppearanceByItemId.TryGetValue(itemId, out value))
            {
                error = $"TbWeaponAppearance[{itemId}].ItemId 不存在";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
        {
            values = null;
            if (!EnsureValid(out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(mapId))
            {
                error = "TbMapSpawn.MapId 为空";
                return false;
            }

            var result = Tables.TbMapSpawn.DataList
                .Where(item => item.MapId == mapId)
                .Select(ToDefinition)
                .ToList();
            values = result;
            return true;
        }

        public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbPlayer.GetOrDefault(id);
            if (row == null)
            {
                error = $"TbPlayer[{id}].Id 不存在";
                return false;
            }

            value = new PlayerConfigDefinition(row.Id, ResolveFileName(ResourceAddressKind.CharacterPrefab, row.PrefabName), row.MaxHP, row.AttackDamage, row.AttackRange, row.AttackCooldownSeconds, row.MoveSpeed, row.TurnSpeed, row.BaseRecoveryPerSecond,row.UnarmedAttackDamage,row.UnarmedAttackRange,row.UnarmedAttackCooldownSeconds,row.UnarmedMeleeArcDegrees,row.BuildingOperateDistance);
            return true;
        }

        public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbEnemy.GetOrDefault(id);
            if (row == null)
            {
                error = $"TbEnemy[{id}].Id 不存在";
                return false;
            }

            value = new EnemyConfigDefinition(
                row.Id,
                ResolveFileName(ResourceAddressKind.CharacterPrefab, row.PrefabName),
                row.MaxHP,
                row.AttackDamage,
                row.AggroRange,
                row.AttackRange,
                row.AttackCooldownSeconds,
                row.MoveSpeed,
                row.LootConfigId,
                row.AttackKind,
                row.RetreatDistance,
                row.TelegraphSeconds,
                row.ProjectileConfigId,
                row.GuaranteedWeaponLootConfigId,
                row.GuaranteedAmmoLootConfigId,
                row.AmmoCapacity,
                row.FallbackMeleeAttackRange,
                row.MagazineCapacity,
                row.ReloadSeconds,
                row.WeaponId);
            return true;
        }

        public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            var row = Tables.TbLoot.GetOrDefault(id);
            if (row == null)
            {
                error = $"TbLoot[{id}].Id 不存在";
                return false;
            }

            value = new LootConfigDefinition(row.Id, ResolveFileName(ResourceAddressKind.SceneObjectPrefab, row.PrefabName), row.ItemId, row.Amount);
            return true;
        }

        public bool TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition value, out string error)
        {
            value = null;
            if (!EnsureValid(out error)) return false;
            value = arcadeCatalog;
            error = null;
            return true;
        }

        public bool TryGetSlayerChallenge(out FirepowerLoop.Arcade.ArcadeSlayerChallengeConfiguration value,out string error)
        {value=null;if(!EnsureValid(out error))return false;value=slayerChallenge;return value!=null;}

        public bool TryGetSlayerMart(out FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration value,out string error)
        {
            value=null;if(!EnsureValid(out error))return false;
            value=slayerMart;error=null;return value!=null;
        }

        /// <summary>读取地图显示范围；允许多个历史道具指向同一地图，但要求至少一个地图道具入口。</summary>
        public bool TryGetMapDisplay(string mapId, out MapDisplayDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error)) return false;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                error = "TbMapDisplay.MapId 为空";
                return false;
            }

            var row = Tables.TbMapDisplay.GetOrDefault(mapId);
            if (row == null)
            {
                error = $"TbMapDisplay[{mapId}].MapId 不存在";
                return false;
            }
            if (Tables.TbMap.GetOrDefault(mapId) == null)
            {
                error = $"TbMapDisplay[{mapId}].MapId 引用不存在";
                return false;
            }
            if (!IsPositiveFinite(row.Width) || !IsPositiveFinite(row.Height) ||
                row.ViewportColumns <= 0 || row.ViewportRows <= 0 ||
                !IsFinite(row.CenterX) || !IsFinite(row.CenterZ))
            {
                error = $"TbMapDisplay[{mapId}] 边界无效";
                return false;
            }
            if (!ResourceAddress.TryFromFileName(ResourceAddressKind.MapUiImage, row.ImageName, false, out var address, out var addressError))
            {
                error = $"TbMapDisplay[{mapId}].ImageName 值“{row.ImageName}”无效：{addressError}";
                return false;
            }

            var matches = Tables.TbItem.DataList.Where(item => item.ActionKind == global::ItemActionKind.OpenMap &&
                string.Equals(item.ActionTarget, mapId, StringComparison.Ordinal)).ToArray();
            // 历史地图道具可以共用稳定地图 ID，但每张可显示地图至少需要一个入口。
            if (matches.Length == 0)
            {
                error = $"TbItem.ActionTarget 未绑定地图 {mapId}";
                return false;
            }

            value = new MapDisplayDefinition(row.MapId, address, row.CenterX, row.CenterZ, row.Width, row.Height,
                row.ViewportColumns, row.ViewportRows);
            error = null;
            return true;
        }

        public bool TryGetLootByItemId(int itemId, out LootConfigDefinition value, out string error)
        {
            value = default;
            if (!EnsureValid(out error))
            {
                return false;
            }

            if (!lootByItemId.TryGetValue(itemId, out value))
            {
                error = $"TbLoot.ItemId={itemId} 不存在";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryGetTestCmdCatalog(
            out IReadOnlyList<TestCmdCategoryDefinition> categories,
            out IReadOnlyList<TestCmdEntryDefinition> entries,
            out string error)
        {
            categories = Array.Empty<TestCmdCategoryDefinition>();
            entries = Array.Empty<TestCmdEntryDefinition>();
            if (!EnsureValid(out error))
            {
                return false;
            }

            categories = testCmdCategories;
            entries = testCmdEntries;
            return true;
        }

        private global::Tables Tables => cachedTables;

        /// <summary>首次查询时一次性校验整套真实 Tables 并建立不可变索引；失败会缓存错误并清空全部派生快照，后续查询不返回部分数据。</summary>
        private bool EnsureValid(out string error)
        {
            if (validated)
            {
                error = validationError;
                return string.IsNullOrEmpty(error);
            }

            try
            {
                cachedTables = tablesFactory();
                validationError = Validate(cachedTables);
                if (string.IsNullOrEmpty(validationError))
                {
                    BuildTestCmdCatalogSnapshot(cachedTables, out testCmdCategories, out testCmdEntries);
                    BuildLootByItemIdSnapshot(cachedTables, out lootByItemId);
                    BuildProjectileSnapshot(cachedTables, out projectiles);
                    BuildPlayerWeaponAppearanceSnapshot(cachedTables, out playerWeaponAppearanceByItemId);
                    BuildArcadeSnapshot(cachedTables, out arcadeCatalog);
                    slayerMart=FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration.FromTables(cachedTables);
                    slayerChallenge=FirepowerLoop.Arcade.ArcadeSlayerChallengeConfiguration.FromTables(cachedTables);
                }
                else
                {
                    testCmdCategories = Array.Empty<TestCmdCategoryDefinition>();
                    testCmdEntries = Array.Empty<TestCmdEntryDefinition>();
                    lootByItemId = new Dictionary<int, LootConfigDefinition>();
                    projectiles = Array.Empty<ProjectileConfigDefinition>();
                    playerWeaponAppearanceByItemId = new Dictionary<int, PlayerWeaponAppearanceDefinition>();
                    arcadeCatalog = null;
                    slayerMart=null;
                }
            }
            catch (Exception e)
            {
                cachedTables = null;
                testCmdCategories = Array.Empty<TestCmdCategoryDefinition>();
                testCmdEntries = Array.Empty<TestCmdEntryDefinition>();
                lootByItemId = new Dictionary<int, LootConfigDefinition>();
                projectiles = Array.Empty<ProjectileConfigDefinition>();
                playerWeaponAppearanceByItemId = new Dictionary<int, PlayerWeaponAppearanceDefinition>();
                arcadeCatalog = null;
                slayerMart=null;
                validationError = $"Tables.Constructor 配置读取异常：{e.Message}";
            }

            validated = true;
            error = validationError;
            return string.IsNullOrEmpty(error);
        }

        private static string Validate(global::Tables tables)
        {
            if (tables == null)
            {
                return "Tables 为空";
            }

            var resourceAddressError = ValidateResourceAddresses(tables);
            if (resourceAddressError != null) return resourceAddressError;

            foreach (var row in tables.TbItem.DataList)
            {
                var error = ValidateItem(row);
                if (error != null) return error;
            }


            foreach (var row in tables.TbWeapon.DataList)
            {
                var error = ValidateWeapon(row, tables);
                if (error != null) return error;
            }

            var weaponAppearanceError = ValidateWeaponAppearances(tables);
            if (weaponAppearanceError != null) return weaponAppearanceError;




            foreach (var row in tables.TbProjectile.DataList)
            {
                var error = ValidateProjectile(row, tables);
                if (error != null) return error;
            }

            foreach (var row in tables.TbMap.DataList)
            {
                if (string.IsNullOrWhiteSpace(row.Id)) return "TbMap.Id 为空";
                if (string.IsNullOrWhiteSpace(row.ScenePath)) return $"TbMap[{row.Id}].ScenePath 为空";
            }


            var mapScenePartError = ValidateMapSceneParts(tables);
            if (mapScenePartError != null) return mapScenePartError;

            foreach (var row in tables.TbPlayer.DataList)
            {
                var error = ValidatePlayer(row);
                if (error != null) return error;
            }

            foreach (var row in tables.TbLoot.DataList)
            {
                if (string.IsNullOrWhiteSpace(row.Id)) return "TbLoot.Id 为空";
                if (string.IsNullOrWhiteSpace(row.PrefabName)) return $"TbLoot[{row.Id}].PrefabName 为空";
                if (row.ItemId <= 0) return $"TbLoot[{row.Id}].ItemId 必须为正";
                if (tables.TbItem.GetOrDefault(row.ItemId) == null) return $"TbLoot[{row.Id}].ItemId 引用不存在";
                if (row.Amount <= 0) return $"TbLoot[{row.Id}].Amount 必须为正";
            }

            var itemLootError = ValidateItemLoots(tables);
            if (itemLootError != null) return itemLootError;

            foreach (var row in tables.TbEnemy.DataList)
            {
                var error = ValidateEnemy(row, tables);
                if (error != null) return error;
                error = ValidateEnemyAttack(row, tables);
                if (error != null) return error;
            }




            var arcadeError = ValidateArcade(tables);
            if (arcadeError != null) return arcadeError;

            var testCmdError = ValidateTestCmdCatalog(tables);
            if (testCmdError != null) return testCmdError;

            var spawnIds = new HashSet<string>(StringComparer.Ordinal);
            var playerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in tables.TbMapSpawn.DataList)
            {
                var error = ValidateSpawn(row, tables, spawnIds, playerCounts);
                if (error != null) return error;
            }

            foreach (var map in tables.TbMap.DataList)
            {
                if (!playerCounts.TryGetValue(map.Id, out var count) || count != 1)
                {
                    return $"TbMapSpawn[{map.Id}].ObjectType 每张地图必须恰好一个 Player";
                }
            }

            return null;
        }

        private static void BuildLootByItemIdSnapshot(
            global::Tables tables,
            out IReadOnlyDictionary<int, LootConfigDefinition> values)
        {
            values = tables.TbLoot.DataList
                .GroupBy(row => row.ItemId)
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => new LootConfigDefinition(
                        group.First().Id,
                        ResolveFileName(ResourceAddressKind.SceneObjectPrefab, group.First().PrefabName),
                        group.First().ItemId,
                        group.First().Amount));
        }

        private static void BuildProjectileSnapshot(
            global::Tables tables,
            out IReadOnlyList<ProjectileConfigDefinition> values)
        {
            values = tables.TbProjectile.DataList
                .OrderBy(row => row.Id, StringComparer.Ordinal)
                .Select(ToDefinition)
                .ToArray();
        }

        private static void BuildPlayerWeaponAppearanceSnapshot(
            global::Tables tables,
            out IReadOnlyDictionary<int, PlayerWeaponAppearanceDefinition> values)
        {
            values = tables.TbWeaponAppearance.DataList
                .Select(ToDefinition)
                .ToDictionary(row => row.ItemId);
        }

        private static void BuildTestCmdCatalogSnapshot(
            global::Tables tables,
            out IReadOnlyList<TestCmdCategoryDefinition> categories,
            out IReadOnlyList<TestCmdEntryDefinition> entries)
        {
            categories = tables.TbTestCmdCategory.DataList
                .OrderBy(row => row.Order)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .Select(row => new TestCmdCategoryDefinition(row.Id, row.Order, row.LabelText))
                .ToArray();

            var categoryOrderById = tables.TbTestCmdCategory.DataList.ToDictionary(row => row.Id, row => row.Order, StringComparer.Ordinal);
            entries = tables.TbTestCmdEntry.DataList
                .OrderBy(row => categoryOrderById[row.CategoryId])
                .ThenBy(row => row.Order)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .Select(row => new TestCmdEntryDefinition(
                    row.Id,
                    row.CategoryId,
                    row.Order,
                    row.LabelText,
                    row.CommandTemplate,
                    row.DescriptionText,
                    row.UsageText,
                    row.ExampleText))
                .ToArray();
        }

        private static string ValidateTestCmdCatalog(global::Tables tables)
        {
            var categoryIds = new HashSet<string>(StringComparer.Ordinal);
            var categoryOrders = new HashSet<int>();
            foreach (var row in tables.TbTestCmdCategory.DataList)
            {
                if (string.IsNullOrWhiteSpace(row.Id)) return "TbTestCmdCategory.Id 为空";
                if (string.Equals(row.Id, TestCmdModel.RecentCategoryId, StringComparison.Ordinal)) return $"TbTestCmdCategory[{row.Id}].Id 使用保留 ID";
                if (!categoryIds.Add(row.Id)) return $"TbTestCmdCategory[{row.Id}].Id 重复";
                if (!categoryOrders.Add(row.Order)) return $"TbTestCmdCategory[{row.Id}].Order 重复";
                if (string.IsNullOrWhiteSpace(row.LabelText)) return $"TbTestCmdCategory[{row.Id}].LabelText 为空";
            }

            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            var entryOrdersByCategory = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var row in tables.TbTestCmdEntry.DataList)
            {
                if (string.IsNullOrWhiteSpace(row.Id)) return "TbTestCmdEntry.Id 为空";
                if (string.Equals(row.Id, TestCmdModel.RecentCategoryId, StringComparison.Ordinal)) return $"TbTestCmdEntry[{row.Id}].Id 使用保留 ID";
                if (!entryIds.Add(row.Id)) return $"TbTestCmdEntry[{row.Id}].Id 重复";
                if (string.IsNullOrWhiteSpace(row.CategoryId)) return $"TbTestCmdEntry[{row.Id}].CategoryId 为空";
                if (!categoryIds.Contains(row.CategoryId)) return $"TbTestCmdEntry[{row.Id}].CategoryId 引用不存在";
                if (!entryOrdersByCategory.TryGetValue(row.CategoryId, out var orders))
                {
                    orders = new HashSet<int>();
                    entryOrdersByCategory.Add(row.CategoryId, orders);
                }

                if (!orders.Add(row.Order)) return $"TbTestCmdEntry[{row.Id}].Order 重复";
                if (string.IsNullOrWhiteSpace(row.LabelText)) return $"TbTestCmdEntry[{row.Id}].LabelText 为空";
                if (string.IsNullOrEmpty(row.CommandTemplate)) return $"TbTestCmdEntry[{row.Id}].CommandTemplate 为空";
                if (string.IsNullOrWhiteSpace(row.DescriptionText)) return $"TbTestCmdEntry[{row.Id}].DescriptionText 为空";
                if (string.IsNullOrWhiteSpace(row.UsageText)) return $"TbTestCmdEntry[{row.Id}].UsageText 为空";
                if (string.IsNullOrWhiteSpace(row.ExampleText)) return $"TbTestCmdEntry[{row.Id}].ExampleText 为空";
            }

            return null;
        }

        private static string ValidateItem(global::Item row)
        {
            if (row.Id <= 0) return "TbItem.Id 必须为正";
            if (string.IsNullOrWhiteSpace(row.NameKey)) return $"TbItem[{row.Id}].NameKey 为空";
            if (string.IsNullOrWhiteSpace(row.DescKey)) return $"TbItem[{row.Id}].DescKey 为空";
            if (row.ItemType != global::ItemType.Material &&
                row.ItemType != global::ItemType.Consumable &&
                row.ItemType != global::ItemType.Weapon &&
                row.ItemType != global::ItemType.Ammunition &&
                row.ItemType != global::ItemType.Tool &&
                row.ItemType != global::ItemType.Clothing) return $"TbItem[{row.Id}].ItemType 无效";
            if (row.ItemType == global::ItemType.Clothing)
            {
                if (((int)row.WearSlot < 1 || (int)row.WearSlot > 7) ||
                    string.IsNullOrWhiteSpace(row.AppearanceId) || row.MaxStackAmount != 1 ||
                    row.UseEffect != global::ItemUseEffect.None || row.UseValue != 0 || row.UseDurationSeconds != 0f)
                    return $"TbItem[{row.Id}] 衣物配置无效";
            }
            else if (row.WearSlot != global::ClothingSlot.None || !string.IsNullOrEmpty(row.AppearanceId))
                return $"TbItem[{row.Id}] 非衣物不可声明穿戴数据";
            if (row.MaxStackAmount <= 0) return $"TbItem[{row.Id}].MaxStackAmount 必须为正";
            if ((row.ItemType == global::ItemType.Material ||
                 row.ItemType == global::ItemType.Weapon ||
                 row.ItemType == global::ItemType.Ammunition ||
                 row.ItemType == global::ItemType.Tool) &&
                (row.UseEffect != global::ItemUseEffect.None || row.UseValue != 0 || row.UseDurationSeconds != 0f)) return $"TbItem[{row.Id}].Material 使用效果无效";
            if (row.UseEffect == global::ItemUseEffect.RestoreHealth ||
                row.UseEffect == global::ItemUseEffect.RestorePower ||
                row.UseEffect == global::ItemUseEffect.RestoreWater)
            {
                if (row.ItemType != global::ItemType.Consumable) return $"TbItem[{row.Id}].恢复效果只能用于 Consumable";
                if (row.UseValue <= 0) return $"TbItem[{row.Id}].{row.UseEffect} UseValue 必须为正";
                if (!IsPositiveFinite(row.UseDurationSeconds)) return $"TbItem[{row.Id}].UseDurationSeconds 必须为正";
            }

            if (row.ItemType == global::ItemType.Consumable &&
                row.UseEffect != global::ItemUseEffect.RestoreHealth &&
                row.UseEffect != global::ItemUseEffect.RestorePower &&
                row.UseEffect != global::ItemUseEffect.RestoreWater)
            {
                return $"TbItem[{row.Id}].Consumable 使用效果无效";
            }

            if (row.ItemType != global::ItemType.Consumable &&
                (!IsFinite(row.UseDurationSeconds) || row.UseDurationSeconds != 0f))
            {
                return $"TbItem[{row.Id}].UseDurationSeconds 必须为 0";
            }

            if (row.ItemType == global::ItemType.Tool)
            {
                if (row.ActionKind != global::ItemActionKind.OpenMap && row.ActionKind != global::ItemActionKind.ToggleTorch)
                    return $"TbItem[{row.Id}].ActionKind 无效";
                if (string.IsNullOrWhiteSpace(row.ActionTarget))
                    return $"TbItem[{row.Id}].ActionTarget 为空";
                if (row.MaxStackAmount != 1)
                    return $"TbItem[{row.Id}].MaxStackAmount 必须为 1";
            }
            else if (row.ActionKind != global::ItemActionKind.None || !string.IsNullOrEmpty(row.ActionTarget))
            {
                return $"TbItem[{row.Id}].ActionKind 或 ActionTarget 无效";
            }

            if (row.ActionKind == global::ItemActionKind.ToggleTorch)
            {
                if (!IsPositiveFinite(row.BurnSeconds) || row.MaxStackAmount != 1 || row.ActionTarget != System.IO.Path.GetFileName(row.ActionTarget))
                    return $"TbItem[{row.Id}] 火把燃烧配置无效";
            }
            else if (row.BurnSeconds != 0f) return $"TbItem[{row.Id}] 非火把不可声明燃烧秒数";
            return null;
        }

        private static string ValidateWeapon(global::WeaponConfig row, global::Tables tables)
        {
            if (row == null) return "TbWeapon 行为空";
            if (row.ItemId <= 0) return "TbWeapon.ItemId 必须为正";
            var item = tables.TbItem.GetOrDefault(row.ItemId);
            if (item == null) return $"TbWeapon[{row.ItemId}].ItemId 引用不存在";
            if (item.ItemType != global::ItemType.Weapon) return $"TbWeapon[{row.ItemId}].ItemId 必须引用武器物品";
            if (row.AttackDamage <= 0) return $"TbWeapon[{row.ItemId}].AttackDamage 必须为正";
            if (row.AttackCooldownSeconds <= 0f) return $"TbWeapon[{row.ItemId}].AttackCooldownSeconds 必须为正";
            if (row.AttackRange <= 0f) return $"TbWeapon[{row.ItemId}].AttackRange 必须为正";
            if (float.IsNaN(row.TreeHarvestEfficiency) || float.IsInfinity(row.TreeHarvestEfficiency) || row.TreeHarvestEfficiency < 0f ||
                float.IsNaN(row.StoneHarvestEfficiency) || float.IsInfinity(row.StoneHarvestEfficiency) || row.StoneHarvestEfficiency < 0f)
                return $"TbWeapon[{row.ItemId}] 采集效率必须为有限非负数";

            switch (row.WeaponKind)
            {
                case global::WeaponKind.Melee:
                    if (row.MeleeArcDegrees <= 0f || row.MeleeArcDegrees > 360f) return $"TbWeapon[{row.ItemId}].MeleeArcDegrees 无效";
                    if (!string.IsNullOrWhiteSpace(row.ProjectileConfigId)) return $"TbWeapon[{row.ItemId}].ProjectileConfigId 必须为空";
                    if (row.AmmoItemId != 0) return $"TbWeapon[{row.ItemId}].AmmoItemId 必须为 0";
                    if (row.MagazineCapacity != 0 || row.ReloadSeconds != 0f) return $"TbWeapon[{row.ItemId}] 近战武器弹匣参数必须为 0";
                    return null;
                case global::WeaponKind.Projectile:
                    if (row.MeleeArcDegrees != 0f) return $"TbWeapon[{row.ItemId}].MeleeArcDegrees 必须为 0";
                    if (string.IsNullOrWhiteSpace(row.ProjectileConfigId)) return $"TbWeapon[{row.ItemId}].ProjectileConfigId 为空";
                    if (row.AmmoItemId <= 0) return $"TbWeapon[{row.ItemId}].AmmoItemId 必须为正";
                    if (row.MagazineCapacity <= 0 || row.ReloadSeconds <= 0f) return $"TbWeapon[{row.ItemId}] 投射物武器弹匣参数必须为正";
                    if (tables.TbProjectile.GetOrDefault(row.ProjectileConfigId) == null) return $"TbWeapon[{row.ItemId}].ProjectileConfigId 引用不存在";
                    var ammo = tables.TbItem.GetOrDefault(row.AmmoItemId);
                    if (ammo == null) return $"TbWeapon[{row.ItemId}].AmmoItemId 引用不存在";
                    if (ammo.ItemType != global::ItemType.Ammunition) return $"TbWeapon[{row.ItemId}].AmmoItemId 必须引用弹药物品";
                    return null;
                default:
                    return $"TbWeapon[{row.ItemId}].WeaponKind 无效";
            }
        }

        private static string ValidateWeaponAppearances(global::Tables tables)
        {
            var itemIds = new HashSet<int>();
            foreach (var row in tables.TbWeaponAppearance.DataList)
            {
                var error = ValidateWeaponAppearance(row, tables);
                if (error != null) return error;
                if (!itemIds.Add(row.ItemId)) return $"TbWeaponAppearance[{row.ItemId}].ItemId 重复";
            }

            foreach (var weapon in tables.TbWeapon.DataList)
            {
                if (!itemIds.Contains(weapon.ItemId))
                {
                    return $"TbWeaponAppearance[{weapon.ItemId}].ItemId 缺少武器外观配置";
                }
            }

            return null;
        }

        private static string ValidateWeaponAppearance(global::WeaponAppearanceConfig row, global::Tables tables)
        {
            if (row == null) return "TbWeaponAppearance 行为空";
            if (row.ItemId <= 0) return "TbWeaponAppearance.ItemId 必须为正";
            var item = tables.TbItem.GetOrDefault(row.ItemId);
            if (item == null) return $"TbWeaponAppearance[{row.ItemId}].ItemId 引用不存在";
            if (item.ItemType != global::ItemType.Weapon) return $"TbWeaponAppearance[{row.ItemId}].ItemId 必须引用武器物品";
            var weapon = tables.TbWeapon.GetOrDefault(row.ItemId);
            if (weapon == null) return $"TbWeaponAppearance[{row.ItemId}].ItemId 武器配置不存在";
            if (string.IsNullOrWhiteSpace(row.PrefabName)) return $"TbWeaponAppearance[{row.ItemId}].PrefabName 为空";
            if (!IsFinite(row.LocalPositionX) || !IsFinite(row.LocalPositionY) || !IsFinite(row.LocalPositionZ)) return $"TbWeaponAppearance[{row.ItemId}].LocalPosition 不是有限数值";
            if (!IsFinite(row.LocalRotationX) || !IsFinite(row.LocalRotationY) || !IsFinite(row.LocalRotationZ)) return $"TbWeaponAppearance[{row.ItemId}].LocalRotation 不是有限数值";
            if (!IsPositiveFinite(row.LocalScaleX) || !IsPositiveFinite(row.LocalScaleY) || !IsPositiveFinite(row.LocalScaleZ)) return $"TbWeaponAppearance[{row.ItemId}].LocalScale 必须为正有限数值";

            switch (row.AttachPoint)
            {
                case global::PlayerWeaponAttachPoint.MeleeWeapon:
                    return weapon.WeaponKind == global::WeaponKind.Melee
                        ? null
                        : $"TbWeaponAppearance[{row.ItemId}].AttachPoint 必须匹配投射武器挂点";
                case global::PlayerWeaponAttachPoint.RangedWeapon:
                case global::PlayerWeaponAttachPoint.LeftHandWeapon:
                    return weapon.WeaponKind == global::WeaponKind.Projectile
                        ? null
                        : $"TbWeaponAppearance[{row.ItemId}].AttachPoint 必须匹配近战武器挂点";
                default:
                    return $"TbWeaponAppearance[{row.ItemId}].AttachPoint 无效";
            }
        }

        private static string ValidateItemLoots(global::Tables tables)
        {
            foreach (var item in tables.TbItem.DataList)
            {
                var matches = tables.TbLoot.DataList
                    .Where(loot => loot.ItemId == item.Id)
                    .Select(loot => loot.Id)
                    .ToArray();
                if (matches.Length != 1)
                {
                    return $"TbLoot.ItemId={item.Id} 必须恰好匹配 1 条掉落配置，当前 {matches.Length} 条：{string.Join(",", matches)}";
                }
            }

            return null;
        }

        private static string ValidateProjectile(global::ProjectileConfig row, Tables tables)
        {
            if (row == null) return "TbProjectile 行为空";
            if (string.IsNullOrWhiteSpace(row.Id)) return "TbProjectile.Id 为空";
            if (string.IsNullOrWhiteSpace(row.PrefabName)) return $"TbProjectile[{row.Id}].PrefabName 为空";
            if (row.MoveSpeed <= 0f) return $"TbProjectile[{row.Id}].MoveSpeed 必须为正";
            if (row.MaxTravelDistance <= 0f) return $"TbProjectile[{row.Id}].MaxTravelDistance 必须为正";
            // 空值表示一次性投射物；配置了回收时仍必须引用有效掉落。
            if (!string.IsNullOrWhiteSpace(row.RecoveryLootConfigId) &&
                tables.TbLoot.GetOrDefault(row.RecoveryLootConfigId) == null) return $"TbProjectile[{row.Id}].RecoveryLootConfigId 引用不存在";
            return null;
        }

        private static string ValidatePlayer(global::PlayerConfig row)
        {
            if (string.IsNullOrWhiteSpace(row.Id)) return "TbPlayer.Id 为空";
            if (string.IsNullOrWhiteSpace(row.PrefabName)) return $"TbPlayer[{row.Id}].PrefabName 为空";
            if (row.MaxHP <= 0) return $"TbPlayer[{row.Id}].MaxHP 必须为正";
            if (row.AttackDamage < 0) return $"TbPlayer[{row.Id}].AttackDamage 不能小于 0";
            if (row.AttackRange <= 0) return $"TbPlayer[{row.Id}].AttackRange 必须为正";
            if (row.AttackCooldownSeconds <= 0) return $"TbPlayer[{row.Id}].AttackCooldownSeconds 必须为正";
            if (row.MoveSpeed <= 0) return $"TbPlayer[{row.Id}].MoveSpeed 必须为正";
            if (row.TurnSpeed <= 0) return $"TbPlayer[{row.Id}].TurnSpeed 必须为正";
            if (row.BaseRecoveryPerSecond <= 0) return $"TbPlayer[{row.Id}].BaseRecoveryPerSecond 必须为正";
            if (float.IsNaN(row.BuildingOperateDistance) || float.IsInfinity(row.BuildingOperateDistance) || row.BuildingOperateDistance <= 0f)
                return $"TbPlayer[{row.Id}].BuildingOperateDistance 必须为有限正数";
            return null;
        }

        private static string ValidateEnemy(global::EnemyConfig row, global::Tables tables)
        {
            if (string.IsNullOrWhiteSpace(row.Id)) return "TbEnemy.Id 为空";
            if (string.IsNullOrWhiteSpace(row.PrefabName)) return $"TbEnemy[{row.Id}].PrefabName 为空";
            if (row.MaxHP <= 0) return $"TbEnemy[{row.Id}].MaxHP 必须为正";
            if (row.AttackDamage < 0) return $"TbEnemy[{row.Id}].AttackDamage 不能小于 0";
            if (row.AggroRange <= 0) return $"TbEnemy[{row.Id}].AggroRange 必须为正";
            if (row.AttackRange <= 0) return $"TbEnemy[{row.Id}].AttackRange 必须为正";
            if (row.AttackRange > row.AggroRange) return $"TbEnemy[{row.Id}].AttackRange 不能大于 AggroRange";
            if (row.AttackCooldownSeconds <= 0) return $"TbEnemy[{row.Id}].AttackCooldownSeconds 必须为正";
            if (row.MoveSpeed <= 0) return $"TbEnemy[{row.Id}].MoveSpeed 必须为正";
            if (!string.IsNullOrWhiteSpace(row.LootConfigId) && tables.TbLoot.GetOrDefault(row.LootConfigId) == null) return $"TbEnemy[{row.Id}].LootConfigId 引用不存在";
            var guaranteedWeaponError = ValidateEnemyGuaranteedWeapon(row, tables);
            if (guaranteedWeaponError != null) return guaranteedWeaponError;
            return null;
        }

        private static string ValidateEnemyAttack(global::EnemyConfig row, global::Tables tables)
        {
            // Arcade 表只提供稳定身份及近远程类别；伤害和攻击时序仍由 Enemy 行决定。
            if (string.IsNullOrWhiteSpace(row.WeaponId))
                return $"TbEnemy[{row.Id}].WeaponId 为空";
            var configuredWeapon = tables.TbArcadeWeapon.GetOrDefault(row.WeaponId);
            if (configuredWeapon == null)
                return $"TbEnemy[{row.Id}].WeaponId={row.WeaponId} 在 TbArcadeWeapon 中不存在";
            bool usesProjectile;
            switch (configuredWeapon.Behavior)
            {
                case global::ArcadeWeaponBehavior.Melee:
                    usesProjectile = false;
                    break;
                case global::ArcadeWeaponBehavior.Direct:
                case global::ArcadeWeaponBehavior.Piercing:
                case global::ArcadeWeaponBehavior.Explosive:
                case global::ArcadeWeaponBehavior.TimedExplosive:
                    usesProjectile = true;
                    break;
                default:
                    return $"TbEnemy[{row.Id}].WeaponId={row.WeaponId} 的 TbArcadeWeapon.Behavior 无效：{configuredWeapon.Behavior}";
            }
            if (usesProjectile != (row.AttackKind == global::EnemyAttackKind.Projectile))
                return $"TbEnemy[{row.Id}].WeaponId={row.WeaponId} 与 AttackKind 不匹配";
            switch (row.AttackKind)
            {
                case global::EnemyAttackKind.Melee:
                    if (row.RetreatDistance != 0f) return $"TbEnemy[{row.Id}].RetreatDistance must be 0";
                    if (row.TelegraphSeconds != 0f) return $"TbEnemy[{row.Id}].TelegraphSeconds must be 0";
                    if (!string.IsNullOrWhiteSpace(row.ProjectileConfigId)) return $"TbEnemy[{row.Id}].ProjectileConfigId must be empty";
                    if (!string.IsNullOrWhiteSpace(row.GuaranteedAmmoLootConfigId)) return $"TbEnemy[{row.Id}].GuaranteedAmmoLootConfigId must be empty";
                    if (row.AmmoCapacity != 0) return $"TbEnemy[{row.Id}].AmmoCapacity must be 0";
                    if (row.FallbackMeleeAttackRange != 0f) return $"TbEnemy[{row.Id}].FallbackMeleeAttackRange must be 0";
                    if (row.MagazineCapacity != 0 || row.ReloadSeconds != 0f) return $"TbEnemy[{row.Id}] melee reload parameters must be 0";
                    return null;
                case global::EnemyAttackKind.Projectile:
                    if (row.RetreatDistance <= 0f || row.RetreatDistance >= row.AttackRange) return $"TbEnemy[{row.Id}].RetreatDistance is invalid";
                    if (row.TelegraphSeconds <= 0f) return $"TbEnemy[{row.Id}].TelegraphSeconds must be positive";
                    if (string.IsNullOrWhiteSpace(row.ProjectileConfigId)) return $"TbEnemy[{row.Id}].ProjectileConfigId is empty";
                    if (tables.TbProjectile.GetOrDefault(row.ProjectileConfigId) == null) return $"TbEnemy[{row.Id}].ProjectileConfigId reference is missing";
                    if (row.AmmoCapacity <= 0) return $"TbEnemy[{row.Id}].AmmoCapacity must be positive";
                    if (row.MagazineCapacity <= 0 || row.MagazineCapacity > row.AmmoCapacity || row.ReloadSeconds <= 0f) return $"TbEnemy[{row.Id}] projectile reload parameters are invalid";
                    if (string.IsNullOrWhiteSpace(row.GuaranteedAmmoLootConfigId)) return $"TbEnemy[{row.Id}].GuaranteedAmmoLootConfigId is empty";
                    var ammoError = ValidateEnemyAmmoLoot(row, tables);
                    if (ammoError != null) return ammoError;
                    if (row.FallbackMeleeAttackRange <= 0f || row.FallbackMeleeAttackRange > row.AggroRange) return $"TbEnemy[{row.Id}].FallbackMeleeAttackRange is invalid";
                    return null;
                default:
                    return $"TbEnemy[{row.Id}].AttackKind is invalid";
            }
        }

        private static string ValidateEnemyGuaranteedWeapon(global::EnemyConfig row, global::Tables tables)
        {
            if (string.IsNullOrWhiteSpace(row.GuaranteedWeaponLootConfigId))
            {
                // 空手僵尸没有必掉武器；仅在配置了掉落时校验武器物品引用。
                return null;
            }

            var loot = tables.TbLoot.GetOrDefault(row.GuaranteedWeaponLootConfigId);
            if (loot == null)
            {
                return $"TbEnemy[{row.Id}].GuaranteedWeaponLootConfigId 引用不存在";
            }

            if (loot.Amount != 1)
            {
                return $"TbLoot[{loot.Id}].Amount 必须为 1";
            }

            var item = tables.TbItem.GetOrDefault(loot.ItemId);
            if (item == null)
            {
                return $"TbLoot[{loot.Id}].ItemId 引用不存在";
            }

            return item.ItemType == global::ItemType.Weapon
                ? null
                : $"TbLoot[{loot.Id}].ItemId 必须引用武器物品";
        }

        private static string ValidateEnemyAmmoLoot(global::EnemyConfig row, global::Tables tables)
        {
            var loot = tables.TbLoot.GetOrDefault(row.GuaranteedAmmoLootConfigId);
            if (loot == null)
            {
                return $"TbEnemy[{row.Id}].GuaranteedAmmoLootConfigId 引用不存在";
            }

            var item = tables.TbItem.GetOrDefault(loot.ItemId);
            if (item == null)
            {
                return $"TbLoot[{loot.Id}].ItemId 引用不存在";
            }

            return item.ItemType == global::ItemType.Ammunition
                ? null
                : $"TbLoot[{loot.Id}].ItemId 必须引用弹药物品";
        }

        private static string ValidateSpawn(global::MapSpawnConfig row, global::Tables tables, HashSet<string> spawnIds, Dictionary<string, int> playerCounts)
        {
            if (string.IsNullOrWhiteSpace(row.Id)) return "TbMapSpawn.Id 为空";
            if (!spawnIds.Add(row.Id)) return $"TbMapSpawn[{row.Id}].Id 重复";
            if (string.IsNullOrWhiteSpace(row.MapId)) return $"TbMapSpawn[{row.Id}].MapId 为空";
            if (tables.TbMap.GetOrDefault(row.MapId) == null) return $"TbMapSpawn[{row.Id}].MapId 引用不存在";
            if (string.IsNullOrWhiteSpace(row.ConfigId)) return $"TbMapSpawn[{row.Id}].ConfigId 为空";
            if (!IsFinite(row.PositionX) || !IsFinite(row.PositionY) || !IsFinite(row.PositionZ)) return $"TbMapSpawn[{row.Id}].Position 不是有限数值";
            if (!IsFinite(row.RotationY)) return $"TbMapSpawn[{row.Id}].RotationY 不是有限数值";

            switch (row.ObjectType)
            {
                case MapSpawnObjectType.Player:
                    if (tables.TbPlayer.GetOrDefault(row.ConfigId) == null) return $"TbMapSpawn[{row.Id}].ConfigId 玩家引用不存在";
                    playerCounts[row.MapId] = playerCounts.TryGetValue(row.MapId, out var count) ? count + 1 : 1;
                    break;
                case MapSpawnObjectType.Enemy:
                    if (tables.TbEnemy.GetOrDefault(row.ConfigId) == null) return $"TbMapSpawn[{row.Id}].ConfigId 敌人引用不存在";
                    break;
                case MapSpawnObjectType.Loot:
                    if (tables.TbLoot.GetOrDefault(row.ConfigId) == null) return $"TbMapSpawn[{row.Id}].ConfigId 物资引用不存在";
                    break;
                default:
                    return $"TbMapSpawn[{row.Id}].ObjectType 无效";
            }

            return null;
        }

        private static MapSpawnDefinition ToDefinition(global::MapSpawnConfig row)
        {
            return new MapSpawnDefinition(row.Id, row.MapId, row.ObjectType, row.ConfigId, row.PositionX, row.PositionY, row.PositionZ, row.RotationY);
        }

        private static WeaponConfigDefinition ToDefinition(global::WeaponConfig row)
        {
            return new WeaponConfigDefinition(
                row.ItemId,
                row.WeaponKind,
                row.AttackDamage,
                row.AttackCooldownSeconds,
                row.AttackRange,
                row.MeleeArcDegrees,
                row.ProjectileConfigId,
                row.AmmoItemId,
                row.MagazineCapacity,
                row.ReloadSeconds,
                row.ReloadWholeMagazine,
                row.TreeHarvestEfficiency,
                row.StoneHarvestEfficiency);
        }

        private static ProjectileConfigDefinition ToDefinition(global::ProjectileConfig row)
        {
            return new ProjectileConfigDefinition(
                row.Id,
                ResolveFileName(ResourceAddressKind.ProjectilePrefab, row.PrefabName),
                row.MoveSpeed,
                row.MaxTravelDistance,
                row.RecoveryLootConfigId);
        }

        private static PlayerWeaponAppearanceDefinition ToDefinition(global::WeaponAppearanceConfig row)
        {
            return new PlayerWeaponAppearanceDefinition(
                row.ItemId,
                ResolveFileName(ResourceAddressKind.WeaponAppearancePrefab, row.PrefabName),
                row.AttachPoint,
                new UnityEngine.Vector3(row.LocalPositionX, row.LocalPositionY, row.LocalPositionZ),
                new UnityEngine.Vector3(row.LocalRotationX, row.LocalRotationY, row.LocalRotationZ),
                new UnityEngine.Vector3(row.LocalScaleX, row.LocalScaleY, row.LocalScaleZ));
        }

        private static string ValidateMapSceneParts(global::Tables tables)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var ordersByMap = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var pathsByMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var map in tables.TbMap.DataList)
            {
                pathsByMap[map.Id] = new HashSet<string>(StringComparer.Ordinal) { map.ScenePath };
            }

            foreach (var row in tables.TbMapScenePart.DataList)
            {
                if (string.IsNullOrWhiteSpace(row.Id)) return "TbMapScenePart.Id 为空";
                if (!ids.Add(row.Id)) return $"TbMapScenePart[{row.Id}].Id 重复";
                if (string.IsNullOrWhiteSpace(row.MapId)) return $"TbMapScenePart[{row.Id}].MapId 为空";
                if (tables.TbMap.GetOrDefault(row.MapId) == null)
                    return $"TbMapScenePart[{row.Id}].MapId 引用不存在：{row.MapId}";
                if (string.IsNullOrWhiteSpace(row.ScenePath)) return $"TbMapScenePart[{row.Id}].ScenePath 为空";
                if (row.Order <= 0) return $"TbMapScenePart[{row.Id}].Order 必须为正：{row.Order}";

                if (!ordersByMap.TryGetValue(row.MapId, out var orders))
                    ordersByMap[row.MapId] = orders = new HashSet<int>();
                if (!orders.Add(row.Order))
                    return $"TbMapScenePart[{row.Id}].Order 在地图 {row.MapId} 中重复：{row.Order}";

                var paths = pathsByMap[row.MapId];
                if (!paths.Add(row.ScenePath))
                    return $"TbMapScenePart[{row.Id}].ScenePath 在地图 {row.MapId} 中重复：{row.ScenePath}";
            }

            return null;
        }

        private static string ValidateResourceAddresses(global::Tables tables)
        {
            foreach (var row in tables.TbItem.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.ItemIcon, row.IconName, true, "TbItem", row.Id, "IconName");
                if (error != null) return error;
            }

            foreach (var row in tables.TbMap.DataList)
            {
                var error = ValidateRelativePath(ResourceAddressKind.MapScene, row.ScenePath, "TbMap", row.Id, "ScenePath");
                if (error != null) return error;
            }

            foreach (var row in tables.TbMapScenePart.DataList)
            {
                var error = ValidateRelativePath(ResourceAddressKind.MapScene, row.ScenePath, "TbMapScenePart", row.Id, "ScenePath");
                if (error != null) return error;
            }

            foreach (var row in tables.TbPlayer.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.CharacterPrefab, row.PrefabName, false, "TbPlayer", row.Id, "PrefabName");
                if (error != null) return error;
            }

            foreach (var row in tables.TbEnemy.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.CharacterPrefab, row.PrefabName, false, "TbEnemy", row.Id, "PrefabName");
                if (error != null) return error;
            }


            foreach (var row in tables.TbWeaponAppearance.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.WeaponAppearancePrefab, row.PrefabName, false, "TbWeaponAppearance", row.ItemId, "PrefabName");
                if (error != null) return error;
            }

            foreach (var row in tables.TbLoot.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.SceneObjectPrefab, row.PrefabName, false, "TbLoot", row.Id, "PrefabName");
                if (error != null) return error;
            }

            foreach (var row in tables.TbArcadeDrop.DataList.Concat(tables.TbArcadeSoloDrop.DataList))
            {
                var error = ValidateFileName(ResourceAddressKind.SceneObjectPrefab, row.VisualPrefabName, true,
                    "TbArcadeDrop", row.Id, "VisualPrefabName");
                if (error != null) return error;
            }

            foreach (var row in tables.TbArcadePowerup.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.SceneObjectPrefab, row.ActiveVisualPrefabName, false,
                    "TbArcadePowerup", row.Id, "ActiveVisualPrefabName");
                if (error != null) return error;
            }

            foreach (var row in tables.TbArcadeSupportVisual.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.SceneObjectPrefab, row.ActiveVisualPrefabName, true,
                    "TbArcadeSupportVisual", row.Id, "ActiveVisualPrefabName");
                if (error != null) return error;
            }


            foreach (var row in tables.TbProjectile.DataList)
            {
                var error = ValidateFileName(ResourceAddressKind.ProjectilePrefab, row.PrefabName, false, "TbProjectile", row.Id, "PrefabName");
                if (error != null) return error;
            }


            return null;
        }

        private static string ValidateFileName(ResourceAddressKind kind, string value, bool optional, string tableName, object key, string fieldName)
        {
            return ResourceAddress.TryFromFileName(kind, value, optional, out _, out var error)
                ? null
                : $"{tableName}[{key}].{fieldName} 值“{value ?? "<null>"}”无效：{error}";
        }

        private static string ValidateRelativePath(ResourceAddressKind kind, string value, string tableName, object key, string fieldName)
        {
            return ResourceAddress.TryFromRelativePath(kind, value, out _, out var error)
                ? null
                : $"{tableName}[{key}].{fieldName} 值“{value ?? "<null>"}”无效：{error}";
        }

        private static string ResolveFileName(ResourceAddressKind kind, string value, bool optional = false)
        {
            if (ResourceAddress.TryFromFileName(kind, value, optional, out var address, out _))
            {
                return address;
            }

            throw new InvalidOperationException($"已验证的资源文件名解析失败：{kind} / {value}");
        }

        private static string ResolveRelativePath(ResourceAddressKind kind, string value)
        {
            if (ResourceAddress.TryFromRelativePath(kind, value, out var address, out _))
            {
                return address;
            }

            throw new InvalidOperationException($"已验证的资源相对路径解析失败：{kind} / {value}");
        }

        /// <summary>街机目录在首次加载时整体校验，防止波次、商品和升级之间出现半可用引用。</summary>
        private static string ValidateArcade(global::Tables tables)
        {
            try
            {
                var layouts = tables.TbArcadeRoundLayout.DataList.Select(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.FromConfig).ToArray();
                var layoutRule = tables.TbArcadeRule.DataList.FirstOrDefault();
                if (layoutRule == null || layouts.Length != tables.TbArcadeArena.DataList.Count * 4 ||
                    layouts.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != layouts.Length)
                    return "轮级布局目录不完整或重复";
                foreach (var arena in tables.TbArcadeArena.DataList)
                {
                    var rounds = layouts.Where(p => p.ArenaConfigNumber == arena.ArenaNumber).ToArray();
                    if (rounds.Length != 4 || rounds.Select(p => p.RoundNumber).Distinct().Count() != 4 ||
                        rounds.Select(p => p.ThemeKey).Distinct(StringComparer.Ordinal).Count() != 1 ||
                        rounds.Any(p => !p.FitsArena(layoutRule.ArenaHalfWidth, layoutRule.ArenaHalfHeight, layoutRule.PlayerRadius)))
                        return "轮级地图引用、通路或轮次无效：" + arena.ArenaNumber;
                }
                foreach (var row in tables.TbArcadeTreasureRoom.DataList)
                {
                    var room = FirepowerLoop.Arcade.ArcadeTreasureRoomParameters.FromConfig(row);
                    foreach (var reward in room.Rewards.Where(r => r.Kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Weapon))
                    {
                        var drop = tables.TbArcadeDrop.GetOrDefault(reward.DropId);
                        if (drop == null || drop.Kind != ArcadeDropKind.TemporaryWeapon) return "军械库武器掉落引用无效：" + reward.DropId;
                    }
                    var target = tables.TbArcadeArena.GetOrDefault(room.ArenaConfigNumber);
                    var arenaRule = tables.TbArcadeRule.DataList.FirstOrDefault();
                    if (target == null || room.WaveNumber > target.WaveGroupIds.Length || arenaRule == null ||
                        !room.FitsArena(arenaRule.ArenaHalfWidth, arenaRule.ArenaHalfHeight, arenaRule.PlayerRadius)) return "宝藏房地图、轮次或边界无效";
                }
            }
            catch (System.ArgumentException error) { return error.Message; }
            if (tables.TbArcadeRule.DataList.Count != 1) return "TbArcadeRule 必须恰好包含一行";
            var rule = tables.TbArcadeRule.DataList[0];
            if (rule == null || !string.Equals(rule.Id, "arcade.default", StringComparison.Ordinal)) return "TbArcadeRule[arcade.default] 缺失";
            if (string.IsNullOrWhiteSpace(rule.ConfigFingerprint) || rule.ConfigFingerprint.Length > 128 ||
                rule.InitialLives <= 0 || rule.InitialBombs < 0 || rule.InitialDashes < 0 || string.IsNullOrWhiteSpace(rule.BaseWeaponId) ||
                rule.MaxAliveEnemies <= 0 || rule.MaxProjectiles <= 0 || rule.MaxDrops <= 0 || rule.BossEveryArenas <= 0 ||
                !IsNonNegativeFinite(rule.HealthGrowthPerArena) || !IsNonNegativeFinite(rule.DamageGrowthPerArena) ||
                !IsNonNegativeFinite(rule.SpeedGrowthPerArena) || !IsNonNegativeFinite(rule.GoldGrowthPerArena) ||
                rule.PlayerMaxHealth <= 0 || !IsPositiveFinite(rule.PlayerMoveSpeed) || !IsPositiveFinite(rule.PlayerRadius) ||
                !IsPositiveFinite(rule.EnemyContactRadius) || !IsPositiveFinite(rule.ContactInvulnerabilitySeconds) ||
                !IsPositiveFinite(rule.RespawnDelaySeconds) || !IsPositiveFinite(rule.ArenaHalfWidth) ||
                !IsPositiveFinite(rule.ArenaHalfHeight) || !IsFinite(rule.DropChance) || rule.DropChance < 0f || rule.DropChance > 1f ||
                !IsPositiveFinite(rule.SupportFireIntervalSeconds) || !IsPositiveFinite(rule.CoopDownedSeconds) ||
                !IsPositiveFinite(rule.WeaponDeliverySeconds) || !IsPositiveFinite(rule.CoopRescueRadius) || !IsPositiveFinite(rule.CoopRescueSpeed) || rule.CoopRescueSpeed<1 || rule.CoopDonationMinLives<2)
                return "TbArcadeRule[arcade.default] 数值无效";

            var weaponIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeWeapon.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !weaponIds.Add(row.Id) || row.Damage <= 0 ||
                    !IsPositiveFinite(row.FireIntervalSeconds) ||
                    (row.Behavior == global::ArcadeWeaponBehavior.Melee
                        ? row.ProjectileSpeed != 0f || row.ProjectileLifetimeSeconds != 0f
                        : !IsPositiveFinite(row.ProjectileSpeed) || !IsPositiveFinite(row.ProjectileLifetimeSeconds)) ||
                    row.ProjectileCount <= 0 || row.ProjectileCount > 12 ||
                    !IsNonNegativeFinite(row.SpreadDegrees) || row.SpreadDegrees > 180f ||
                    !Enum.IsDefined(typeof(global::ArcadeWeaponBehavior), row.Behavior) || row.MaxHits <= 0 ||
                    !IsNonNegativeFinite(row.ExplosionRadius) ||
                    (row.Behavior == global::ArcadeWeaponBehavior.Melee
                        ? !IsPositiveFinite(row.MeleeRange) || !IsPositiveFinite(row.MeleeArcDegrees) || row.MeleeArcDegrees > 360f
                        : row.MeleeRange != 0f || row.MeleeArcDegrees != 0f) ||
                    (row.Behavior == global::ArcadeWeaponBehavior.Melee &&
                     (row.ProjectileCount != 1 || row.SpreadDegrees != 0f || row.MaxHits != 1 ||
                      row.ExplosionRadius != 0f || !string.IsNullOrWhiteSpace(row.ProjectileVisualResourceName))) ||
                    (row.Behavior == global::ArcadeWeaponBehavior.Direct && (row.MaxHits != 1 || row.ExplosionRadius != 0f)) ||
                    (row.Behavior == global::ArcadeWeaponBehavior.Piercing && (row.MaxHits <= 1 || row.ExplosionRadius != 0f)) ||
                    ((row.Behavior == global::ArcadeWeaponBehavior.Explosive ||
                      row.Behavior == global::ArcadeWeaponBehavior.TimedExplosive) &&
                     (row.MaxHits != 1 || row.ExplosionRadius <= 0f)))
                    return $"TbArcadeWeapon[{row?.Id ?? "<null>"}] 数值无效";
            }
            if (!weaponIds.Contains(rule.BaseWeaponId)) return "TbArcadeRule.BaseWeaponId 引用不存在";

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeEnemyGroup.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !groupIds.Add(row.Id)) return $"TbArcadeEnemyGroup[{row?.Id ?? "<null>"}].Id 为空或重复";
                if (row.Members == null || row.Members.Length == 0 || !IsPositiveFinite(row.SpawnIntervalSeconds) || row.MaxAlive <= 0 || row.MaxAlive > rule.MaxAliveEnemies ||
                    !IsPositiveFinite(row.HealthMultiplier) || !IsPositiveFinite(row.DamageMultiplier) || !IsPositiveFinite(row.SpeedMultiplier) || row.ClearGold < 0)
                    return $"TbArcadeEnemyGroup[{row.Id}] 数值无效";
                var enemyIds = new HashSet<string>(StringComparer.Ordinal);
                var total = 0;
                foreach (var member in row.Members)
                {
                    if (member == null || string.IsNullOrWhiteSpace(member.EnemyId) || !enemyIds.Add(member.EnemyId) ||
                        tables.TbEnemy.GetOrDefault(member.EnemyId) == null || member.Count <= 0)
                        return $"TbArcadeEnemyGroup[{row.Id}].Members 引用或数量无效";
                    total = checked(total + member.Count);
                }
                if (row.MaxAlive > total) return $"TbArcadeEnemyGroup[{row.Id}].MaxAlive 不能大于敌人总数";
            }

            var arenas = tables.TbArcadeArena.DataList.OrderBy(row => row.ArenaNumber).ToArray();
            if (arenas.Length == 0) return "TbArcadeArena 不能为空";
            var bossIds = new HashSet<string>(StringComparer.Ordinal);
            var bossesById = new Dictionary<string, global::ArcadeBossConfig>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeBoss.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !bossIds.Add(row.Id) ||
                    string.IsNullOrWhiteSpace(row.EnemyGroupId) || !groupIds.Contains(row.EnemyGroupId) ||
                    !IsPositiveFinite(row.SkillCooldownSeconds) || !IsPositiveFinite(row.TelegraphSeconds) ||
                    !IsPositiveFinite(row.ChargeDurationSeconds) || !IsPositiveFinite(row.ChargeSpeedMultiplier) ||
                    row.ChargeSpeedMultiplier <= 1f || !IsPositiveFinite(row.ImpactRadius) ||
                    !IsPositiveFinite(row.ImpactDamageMultiplier))
                    return $"TbArcadeBoss[{row?.Id ?? "<null>"}] 数值或敌群引用无效";
                var bossGroup = tables.TbArcadeEnemyGroup.GetOrDefault(row.EnemyGroupId);
                if (bossGroup?.Members == null || bossGroup.Members.Length != 1 || bossGroup.Members[0].Count != 1)
                    return $"TbArcadeBoss[{row.Id}].EnemyGroupId 必须引用只含一个敌人的敌群";
                bossesById.Add(row.Id, row);
                if (row.RayIntervalSeconds == 0f ? row.RayTelegraphSeconds != 0f || row.RaySpeed != 0f || row.RayDamageMultiplier != 0f : (!IsPositiveFinite(row.RayIntervalSeconds) ||
                    !IsPositiveFinite(row.RayTelegraphSeconds) || !IsPositiveFinite(row.RaySpeed) ||
                    !IsPositiveFinite(row.RayDamageMultiplier)))
                    return $"TbArcadeBoss[{row.Id}] 射线数值无效";
                if (!string.IsNullOrEmpty(row.ReinforcementGroupId) &&
                    (!groupIds.Contains(row.ReinforcementGroupId) || !IsPositiveFinite(row.ReinforcementIntervalSeconds)))
                    return $"TbArcadeBoss[{row.Id}] 增援数值或敌群引用无效";
                if (string.IsNullOrEmpty(row.ReinforcementGroupId) && row.ReinforcementIntervalSeconds != 0f)
                    return $"TbArcadeBoss[{row.Id}] 未启用增援时必须为零间隔";
            }
            var challengeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeChallenge.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !challengeIds.Add(row.Id) ||
                    string.IsNullOrWhiteSpace(row.EnemyGroupId) || !groupIds.Contains(row.EnemyGroupId) ||
                    row.StartArena <= 0 || row.Interval <= 0 || !IsPositiveFinite(row.TimeLimitSeconds))
                    return $"TbArcadeChallenge[{row?.Id ?? "<null>"}] 数值或敌群引用无效";
            }
            if (challengeIds.Count == 0) return "TbArcadeChallenge 不能为空";
            var salts = new HashSet<int>();
            for (var index = 0; index < arenas.Length; index++)
            {
                var row = arenas[index];
                var expected = index + 1;
                if (row == null || row.ArenaNumber != expected) return $"TbArcadeArena 必须从 1 连续编号，缺少 {expected}";
                try
                {
                    var opening = FirepowerLoop.Arcade.ArcadeOpeningParameters.FromConfig(row);
                    var content = FirepowerLoop.Arcade.ArcadeBabylonParameters.FromConfig(row);
                    if (content.Crates.Any(c => Math.Abs(c.X) + c.Radius > rule.ArenaHalfWidth || Math.Abs(c.Y) + c.Radius > rule.ArenaHalfHeight))
                        return "箱子位置越界";
                    if (opening.Enabled && (opening.WaterMinY <= -rule.ArenaHalfHeight || opening.WaterMinY >= rule.ArenaHalfHeight ||
                        Math.Abs(opening.CampfireX) + opening.CampfireRadius > rule.ArenaHalfWidth ||
                        Math.Abs(opening.CampfireY) + opening.CampfireRadius > rule.ArenaHalfHeight ||
                        row.WaveGroupIds.Any(id => tables.TbArcadeEnemyGroup.GetOrDefault(id)?.Members.Length != 1)))
                        return $"TbArcadeArena[{row.ArenaNumber}] 岛岸区域越界或开场敌群不是单一敌种";
                }
                catch (ArgumentException exception) { return $"TbArcadeArena[{row.ArenaNumber}] {exception.Message}"; }
                if (row.NextArenaNumbers == null || row.NextArenaNumbers.Length > 4 ||
                    row.NextArenaNumbers.Distinct().Count() != row.NextArenaNumbers.Length ||
                    row.NextArenaNumbers.Any(id => id <= 0 || id == row.ArenaNumber || id > arenas.Length))
                    return $"TbArcadeArena[{row.ArenaNumber}] 路线出口引用无效";
                if (!string.IsNullOrEmpty(row.FinalBossId) && (!bossesById.ContainsKey(row.FinalBossId) || row.NextArenaNumbers.Length != 0))
                    return $"TbArcadeArena[{row.ArenaNumber}] 最终 Boss 引用无效";
                if (row.WaveGroupIds == null || row.WaveGroupIds.Length != 4 || row.WaveGroupIds.Any(id => !groupIds.Contains(id)))
                    return $"TbArcadeArena[{row.ArenaNumber}].WaveGroupIds 必须包含四个有效敌群";
                if (row.Obstacles == null || row.Obstacles.Length > 8)
                    return $"TbArcadeArena[{row.ArenaNumber}].Obstacles 数量无效";
                if (row.Hazards == null || row.Hazards.Length > 8)
                    return $"TbArcadeArena[{row.ArenaNumber}].Hazards 数量无效";
                var hazardIds = new HashSet<int>();
                foreach (var hazard in row.Hazards)
                {
                    if (hazard == null || !hazardIds.Add(hazard.Id)) return $"TbArcadeArena[{row.ArenaNumber}].Hazards 身份无效";
                    var parameters = ToHazardParameters(hazard);
                    var hazardError = FirepowerLoop.Arcade.ArcadeHazardRules.Validate(parameters,
                        rule.ArenaHalfWidth, rule.ArenaHalfHeight, rule.PlayerRadius);
                    if (hazardError != null) return $"TbArcadeArena[{row.ArenaNumber}].Hazards[{hazard.Id}] {hazardError}";
                    foreach (var obstacle in row.Obstacles)
                    {
                        if (obstacle == null) continue;
                        var dx = Math.Max(0f, Math.Abs(parameters.X - obstacle.CenterX) - obstacle.HalfWidth);
                        var dy = Math.Max(0f, Math.Abs(parameters.Y - obstacle.CenterY) - obstacle.HalfHeight);
                        if (dx * dx + dy * dy <= Math.Pow(parameters.Extent + rule.PlayerRadius * 2f, 2))
                            return $"TbArcadeArena[{row.ArenaNumber}].Hazards[{hazard.Id}] 与静态障碍间缺少安全通路";
                    }
                }
                foreach (var obstacle in row.Obstacles)
                {
                    if (obstacle == null || !IsFinite(obstacle.CenterX) || !IsFinite(obstacle.CenterY) ||
                        !IsPositiveFinite(obstacle.HalfWidth) || !IsPositiveFinite(obstacle.HalfHeight) ||
                        Math.Abs(obstacle.CenterX) + obstacle.HalfWidth + rule.PlayerRadius >= rule.ArenaHalfWidth ||
                        Math.Abs(obstacle.CenterY) + obstacle.HalfHeight + rule.PlayerRadius >= rule.ArenaHalfHeight ||
                        Math.Abs(obstacle.CenterX) <= obstacle.HalfWidth + rule.PlayerRadius + 0.5f &&
                        Math.Abs(obstacle.CenterY) <= obstacle.HalfHeight + rule.PlayerRadius + 0.5f)
                        return $"TbArcadeArena[{row.ArenaNumber}].Obstacles 尺寸、边界或中心复活区无效";
                }
                if (row.IsBoss != (row.ArenaNumber % rule.BossEveryArenas == 0)) return $"TbArcadeArena[{row.ArenaNumber}].IsBoss 与 BossEveryArenas 不匹配";
                if (!string.IsNullOrWhiteSpace(row.BossId))
                {
                    if (!row.IsBoss || !bossesById.TryGetValue(row.BossId, out var boss) ||
                        !string.Equals(row.WaveGroupIds[row.WaveGroupIds.Length - 1], boss.EnemyGroupId, StringComparison.Ordinal))
                        return $"TbArcadeArena[{row.ArenaNumber}].BossId 必须引用与最后一轮敌群匹配的 Boss";
                }
                if (row.ClearGold < 0 || !salts.Add(row.EncounterSeedSalt)) return $"TbArcadeArena[{row.ArenaNumber}] 金币或战斗种子盐无效";
            }

            // 深度受十场限制，即使配置有环也不会无界递归或进入无法通关的路线。
            bool ValidateRoute(int id, int depth)
            {
                if (depth > 10) return false;
                var row = arenas[id - 1];
                if (row.NextArenaNumbers.Length == 0) return depth == 10 && !string.IsNullOrEmpty(row.FinalBossId);
                return depth < 10 && string.IsNullOrEmpty(row.FinalBossId) && row.NextArenaNumbers.All(next => ValidateRoute(next, depth + 1));
            }
            if (!ValidateRoute(1, 1)) return "TbArcadeArena 休闲路线必须恰好十场并终止于独立最终 Boss";

            var upgradeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeUpgrade.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !upgradeIds.Add(row.Id) || !Enum.IsDefined(typeof(global::ArcadeUpgradeKind), row.Kind) ||
                    !IsPositiveFinite(row.ValuePerStack) || row.MaxStacks <= 0)
                    return $"TbArcadeUpgrade[{row?.Id ?? "<null>"}] 无效";
            }

            var supportIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeSupportVisual.DataList)
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !supportIds.Add(row.Id) ||
                    string.IsNullOrWhiteSpace(row.ProductId) || row.EquipmentSlotId != ArcadeRunDefaults.SupportSlotId ||
                    !IsFinite(row.VisualOffsetX) || !IsFinite(row.VisualOffsetY) || !IsFinite(row.VisualOffsetZ))
                    return "支援视觉配置无效";

            var dropIds = new HashSet<string>(StringComparer.Ordinal);
            var powerupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadePowerup.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !powerupIds.Add(row.Id) ||
                    !Enum.IsDefined(typeof(global::ArcadePowerupBehavior), row.Behavior) ||
                    !IsNonNegativeFinite(row.Magnitude) || row.Damage < 0 ||
                    (row.Behavior==global::ArcadePowerupBehavior.WeaponSkull&&row.Magnitude!=2f&&row.Magnitude!=3f) ||
                    !IsNonNegativeFinite(row.IntervalSeconds) || !IsNonNegativeFinite(row.Radius) ||
                    !IsNonNegativeFinite(row.ProjectileSpeed) || row.Charges < 0 ||
                    !IsFinite(row.VisualOffsetX) || !IsFinite(row.VisualOffsetY) || !IsFinite(row.VisualOffsetZ))
                    return $"TbArcadePowerup[{row?.Id ?? "<null>"}] 无效";
            }
            // 单人相机道具仅来自客户端专表，不向联机共享表扩散。
            if (tables.TbArcadeDrop.DataList.Any(row => row.Kind == global::ArcadeDropKind.FirstPerson) ||
                tables.TbArcadeSoloDrop.DataList.Any(row => row.Kind != global::ArcadeDropKind.FirstPerson ||
                    row.MinArena != 1 || row.MaxArena != 1 || row.MaxOwned != 1 ||
                    row.ProductId != "arcade.camera.first_person"))
                return "街机第一人称掉落必须限定为第一场单人专表";
            foreach (var row in tables.TbArcadeDrop.DataList.Concat(tables.TbArcadeSoloDrop.DataList))
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !dropIds.Add(row.Id) || row.MinArena <= 0 || row.MaxArena < 0 ||
                    row.MaxArena != 0 && row.MaxArena < row.MinArena || row.Weight <= 0 || !Enum.IsDefined(typeof(global::ArcadeDropKind), row.Kind) ||
                    string.IsNullOrWhiteSpace(row.ProductId) || row.Amount <= 0 || !IsNonNegativeFinite(row.DurationSeconds) || row.MaxOwned < 0)
                    return $"TbArcadeDrop[{row?.Id ?? "<null>"}] 无效";
                var timed = row.Kind == global::ArcadeDropKind.TemporaryWeapon ||
                            row.Kind == global::ArcadeDropKind.Support || row.Kind == global::ArcadeDropKind.Powerup ||
                            row.Kind == global::ArcadeDropKind.FirstPerson;
                if (timed != (row.DurationSeconds > 0f)) return $"TbArcadeDrop[{row.Id}].DurationSeconds 与掉落类型不匹配";
                if (row.Kind == global::ArcadeDropKind.TemporaryWeapon && !weaponIds.Contains(row.ProductId))
                    return $"TbArcadeDrop[{row.Id}].ProductId 武器引用不存在";
                if (row.Kind == global::ArcadeDropKind.Powerup && !powerupIds.Contains(row.ProductId))
                    return $"TbArcadeDrop[{row.Id}].ProductId 强化道具引用不存在";
            }

            var rewardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.TbArcadeReward.DataList)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id) || !rewardIds.Add(row.Id) || !Enum.IsDefined(typeof(global::ArcadeRewardTrigger), row.Trigger) ||
                    row.StartArena <= 0 || row.Interval <= 0 || string.IsNullOrWhiteSpace(row.RewardId) || row.Amount <= 0 || row.AmountPerTrigger < 0)
                    return $"TbArcadeReward[{row?.Id ?? "<null>"}] 无效";
            }
            return null;
        }

        /// <summary>把生成行转换为双端纯规则参数，不把可变生成配置暴露给战斗表现。</summary>
        internal static FirepowerLoop.Arcade.ArcadeHazardParameters ToHazardParameters(global::ArcadeHazardConfig row) =>
            new FirepowerLoop.Arcade.ArcadeHazardParameters(row.Id, (int)row.Kind, row.CenterX, row.CenterY,
                row.DirectionDegrees, row.Radius, row.ArmLength, row.ArmHalfWidth, row.IdleSeconds,
                row.WarningSeconds, row.ActiveSeconds, row.AngularSpeedDegrees, row.HitIntervalSeconds,
                row.PlayerDamageRatio, row.BossDamageRatio);

        private static void BuildArcadeSnapshot(global::Tables tables, out ArcadeConfigCatalogDefinition value)
        {
            var rule = tables.TbArcadeRule.DataList[0];
            value = new ArcadeConfigCatalogDefinition(
                new ArcadeRuleDefinition(rule.Id, rule.InitialLives, rule.InitialBombs, rule.InitialDashes, rule.BaseWeaponId, rule.MaxAliveEnemies, rule.MaxProjectiles, rule.MaxDrops, rule.HealthGrowthPerArena,
                    rule.DamageGrowthPerArena, rule.SpeedGrowthPerArena, rule.GoldGrowthPerArena, rule.BossEveryArenas,
                    rule.PlayerMaxHealth, rule.PlayerMoveSpeed, rule.PlayerRadius, rule.EnemyContactRadius,
                    rule.ContactInvulnerabilitySeconds, rule.RespawnDelaySeconds, rule.ArenaHalfWidth, rule.ArenaHalfHeight,
                    rule.DropChance, rule.SupportFireIntervalSeconds, rule.ConfigFingerprint,
                    rule.CoopDownedSeconds,rule.CoopRescueRadius,rule.CoopRescueSpeed,rule.CoopDonationMinLives,rule.WeaponDeliverySeconds),
                tables.TbArcadeWeapon.DataList.OrderBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeWeaponDefinition(row.Id, row.HeldVisualResourceName,
                        row.ProjectileVisualResourceName, row.MuzzleFlash,
                        new Vector3(row.ProjectileFirePointX, row.ProjectileFirePointY, row.ProjectileFirePointZ),
                        row.Damage, row.FireIntervalSeconds,
                        row.ProjectileSpeed, row.ProjectileLifetimeSeconds, row.ProjectileCount, row.SpreadDegrees,
                        row.Behavior, row.MaxHits, row.ExplosionRadius, row.MeleeRange, row.MeleeArcDegrees)),
                tables.TbArcadeArena.DataList.OrderBy(row => row.ArenaNumber)
                    .Select(row => new ArcadeArenaDefinition(row.ArenaNumber, row.WaveGroupIds,
                        row.Obstacles.Select(obstacle => new ArcadeObstacleDefinition(obstacle.CenterX, obstacle.CenterY,
                            obstacle.HalfWidth, obstacle.HalfHeight)),
                        row.IsBoss, row.BossId, row.ClearGold, row.EncounterSeedSalt, row.Hazards.Select(ToHazardParameters),
                        row.NextArenaNumbers, row.FinalBossId, FirepowerLoop.Arcade.ArcadeOpeningParameters.FromConfig(row), FirepowerLoop.Arcade.ArcadeBabylonParameters.FromConfig(row))),
                tables.TbArcadeEnemyGroup.DataList.OrderBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeEnemyGroupDefinition(row.Id,
                        row.Members.Select(member => new ArcadeEnemyMemberDefinition(member.EnemyId, member.Count)),
                        row.SpawnIntervalSeconds, row.MaxAlive, row.HealthMultiplier, row.DamageMultiplier, row.SpeedMultiplier, row.ClearGold)),
                tables.TbArcadeBoss.DataList.OrderBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeBossDefinition(row.Id, row.EnemyGroupId, row.SkillCooldownSeconds,
                        row.TelegraphSeconds, row.ChargeDurationSeconds, row.ChargeSpeedMultiplier,
                        row.ImpactRadius, row.ImpactDamageMultiplier, row.RayIntervalSeconds,
                        row.RayTelegraphSeconds, row.RaySpeed, row.RayDamageMultiplier,
                        row.ReinforcementGroupId, row.ReinforcementIntervalSeconds)),
                tables.TbArcadeChallenge.DataList.OrderBy(row => row.StartArena).ThenBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeChallengeDefinition(row.Id, row.EnemyGroupId, row.StartArena,
                        row.Interval, row.TimeLimitSeconds)),
                tables.TbArcadeDrop.DataList.Concat(tables.TbArcadeSoloDrop.DataList)
                    .OrderBy(row => row.MinArena).ThenBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeDropDefinition(row.Id, row.MinArena, row.MaxArena, row.Weight, row.Kind,
                        row.ProductId, ResolveFileName(ResourceAddressKind.SceneObjectPrefab, row.VisualPrefabName, true),
                        row.Amount, row.DurationSeconds, row.MaxOwned)),
                tables.TbArcadePowerup.DataList.OrderBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadePowerupDefinition(row.Id, row.Behavior,
                        ResolveFileName(ResourceAddressKind.SceneObjectPrefab, row.ActiveVisualPrefabName),
                        row.VisualFollowPlayer, new Vector3(row.VisualOffsetX, row.VisualOffsetY, row.VisualOffsetZ),
                        row.Magnitude, row.Damage, row.IntervalSeconds, row.Radius, row.ProjectileSpeed, row.Charges)),
                tables.TbArcadeSupportVisual.DataList.OrderBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeSupportVisualDefinition(row.Id, row.ProductId, row.EquipmentSlotId,
                        ResolveFileName(ResourceAddressKind.SceneObjectPrefab, row.ActiveVisualPrefabName, true),
                        new Vector3(row.VisualOffsetX, row.VisualOffsetY, row.VisualOffsetZ))),
                tables.TbArcadeUpgrade.DataList.OrderBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeUpgradeDefinition(row.Id, row.Kind, row.ValuePerStack, row.MaxStacks)),
                tables.TbArcadeReward.DataList.OrderBy(row => row.StartArena).ThenBy(row => row.Id, StringComparer.Ordinal)
                    .Select(row => new ArcadeRewardDefinition(row.Id, row.Trigger, row.StartArena, row.Interval, row.RewardId, row.Amount, row.AmountPerTrigger)),
                tables.TbArcadeTreasureRoom.DataList.Select(FirepowerLoop.Arcade.ArcadeTreasureRoomParameters.FromConfig),
                tables.TbArcadeRoundLayout.DataList.OrderBy(row => row.ArenaConfigNumber).ThenBy(row => row.RoundNumber)
                    .Select(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.FromConfig));
        }

        private static bool IsNonNegativeFinite(float value) => IsFinite(value) && value >= 0f;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsPositiveFinite(float value)
        {
            return IsFinite(value) && value > 0f;
        }
    }
}

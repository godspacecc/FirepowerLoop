using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    public interface IRuntimeConfigProvider : IUtility
    {
        bool TryGetPreparationRule(out BasePreparationRule rule,out string error) { rule=null;error="基地准备配置不可用";return false; }
        bool TryGetItemPreparation(int id,out ItemPreparationDefinition item,out string error) {item=default;error="物品准备配置不可用";return false;}
        bool TryGetMapPreparation(string id,out MapPreparationRule rule,out string error) {rule=null;error="章节准备配置不可用";return false;}
        bool TryGetValleyGenerationProfile(string mapId, out ValleyGenerationProfile value, out string error)
        { value = null; error = "河谷生成配置不可用"; return false; }
        bool TryGetCivilisationCatalog(out CivilisationCatalog value, out string error)
        { value = null; error = "文明探索配置不可用"; return false; }
        bool TryGetItem(int id, out ItemDefinition value, out string error)
        {
            value = default;
            error = $"TbItem[{id}].Id 不存在";
            return false;
        }

        bool TryGetItems(out IReadOnlyList<ItemDefinition> values, out string error)
        {
            values = Array.Empty<ItemDefinition>();
            error = "TbItem 不可用";
            return false;
        }

        bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
        {
            value = default;
            error = $"TbWeapon[{itemId}].ItemId 不存在";
            return false;
        }

        bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
        {
            value = default;
            error = $"TbProjectile[{id}].Id 不存在";
            return false;
        }

        bool TryGetProjectiles(out IReadOnlyList<ProjectileConfigDefinition> values, out string error)
        {
            if (TryGetProjectile("projectile.arrow.basic", out var value, out error))
            {
                values = new[] { value };
                return true;
            }

            values = Array.Empty<ProjectileConfigDefinition>();
            return false;
        }

        bool TryGetPlayerWeaponAppearance(int itemId, out PlayerWeaponAppearanceDefinition value, out string error)
        {
            value = default;
            error = $"TbWeaponAppearance[{itemId}].ItemId 不存在";
            return false;
        }

        bool TryGetCraftRecipe(string id, out CraftRecipeDefinition value, out string error)
        {
            value = null;
            error = $"TbCraftRecipe[{id}].Id 不存在";
            return false;
        }

        bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
        {
            values = Array.Empty<CraftRecipeDefinition>();
            error = "TbCraftRecipe 不可用";
            return false;
        }

        bool TryGetBaseCampLayout(out IReadOnlyList<BaseCampLayoutDefinition> values, out string error)
        {
            values = Array.Empty<BaseCampLayoutDefinition>();
            error = null;
            return true;
        }

        bool TryGetTempBuilding(string id, out TempBuildingDefinition value, out string error)
        {
            value = null;
            error = $"TbTempBuilding[{id}].Id 不存在";
            return false;
        }

        bool TryGetMap(string id, out MapAssetDefinition value, out string error);
        bool TryGetMapDisplay(string mapId, out MapDisplayDefinition value, out string error)
        {
            value = default;
            error = $"TbMapDisplay[{mapId}].MapId 不存在";
            return false;
        }
        bool TryGetExpeditionTasks(string mapId, out IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
        {
            values = Array.Empty<ExpeditionTaskDefinition>();
            error = "TbExpeditionTask 不可用";
            return false;
        }
        bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error);
        /// <summary>读取 NPC 可选对话入口；无绑定成功返回 null，不改变旧 NPC 交互。</summary>
        bool TryGetNpcDialogue(string npcId, out DialogueDefinition value, out string error)
        {
            value = null; error = null; return true;
        }
        bool TryGetNpc(string id, out NpcConfigDefinition value, out string error)
        {
            value = default;
            error = $"TbNpc[{id}].Id 不存在";
            return false;
        }
        bool TryGetShop(string shopId, out ShopDefinition definition, out string error)
        {
            definition = default;
            error = $"TbShop[{shopId}].Id 不存在";
            return false;
        }
        bool TryGetShopItems(string shopId, out IReadOnlyList<ShopItemDefinition> definitions, out string error)
        {
            definitions = Array.Empty<ShopItemDefinition>();
            error = $"TbShopItem.ShopId={shopId} 不可用";
            return false;
        }
        bool TryGetShopItem(string shopId, int itemId, out ShopItemDefinition definition, out string error)
        {
            definition = default;
            error = $"TbShopItem[{shopId},{itemId}] 不存在";
            return false;
        }
        bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error);
        bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error);
        bool TryGetLoot(string id, out LootConfigDefinition value, out string error);
        bool TryGetLootByItemId(int itemId, out LootConfigDefinition value, out string error)
        {
            value = default;
            error = $"TbLoot.ItemId={itemId} 不存在";
            return false;
        }

        bool TryGetTempBuildings(out IReadOnlyList<TempBuildingDefinition> values, out string error)
        {
            values = Array.Empty<TempBuildingDefinition>();
            error = "TbTempBuilding 不可用";
            return false;
        }

        bool TryGetTempBuildingManualAttack(string buildingId, out TempBuildingManualAttackDefinition value)
        {
            value = null;
            return false;
        }

        bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error);

        bool TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition value, out string error)
        {
            value = null;
            error = "街机配置不可用";
            return false;
        }

        bool TryGetTestCmdCatalog(
            out IReadOnlyList<TestCmdCategoryDefinition> categories,
            out IReadOnlyList<TestCmdEntryDefinition> entries,
            out string error)
        {
            categories = Array.Empty<TestCmdCategoryDefinition>();
            entries = Array.Empty<TestCmdEntryDefinition>();
            error = "TestCmd Catalog 不可用";
            return false;
        }
    }
}

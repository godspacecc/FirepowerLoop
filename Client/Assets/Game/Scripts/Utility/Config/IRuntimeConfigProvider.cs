using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    public interface IRuntimeConfigProvider : IUtility
    {
        /// <summary>正式永久角色目录；缺配置明确拒绝购买。</summary>
        bool TryGetSurvivorCharacters(out SurvivorCharacterProduct[] products,out string error)
        {products=null;error="character.config_failed";return false;}

        /// <summary>读取正式入场动作的身体体积；缺失时明确拒绝几何命中，不能退回计时无敌。</summary>
        bool TryGetDockHitPose(string prefabAddress,out FirepowerLoop.Arcade.ArcadeHitPoseSequence value,out string error)
        {value=null;error="码头身体碰撞配置不可用";return false;}
        /// <summary>区分必须提供正式商店配置的数据源与旧测试提供者，避免配置读取失败时静默降级。</summary>
        bool HasSlayerMartConfiguration => false;
        /// <summary>首批Slayer只读规则；缺配置拒绝进入，不在旧提供者伪造默认值。</summary>
        bool TryGetSlayerChallenge(out FirepowerLoop.Arcade.ArcadeSlayerChallengeConfiguration value,out string error)
        {value=null;error="Slayer挑战配置不可用";return false;}

        /// <summary>读取新商店只读目录；旧测试提供者缺省不开放商店。</summary>
        bool TryGetSlayerMart(out FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration value,out string error)
        { value=null;error="SlayerMart配置不可用";return false; }

        /// <summary>独立幸存者规则，旧提供者缺配置明确拒绝。</summary>
        bool TryGetSurvivor(out FirepowerLoop.Arcade.SurvivorConfiguration value, out string error)
        { value = null; error = "幸存者配置不可用"; return false; }

        /// <summary>正式数据源要求完整敌池；旧提供者不伪造新配置。</summary>
        bool HasSurvivorEnemyRoster => false;
        bool TryGetSurvivorEnemyRoster(out SurvivorEnemyRoster value,out string error)
        {value=null;error="幸存者敌池配置不可用";return false;}

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

        bool TryGetMap(string id, out MapAssetDefinition value, out string error);
        bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error);
        bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error);
        bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error);
        bool TryGetLoot(string id, out LootConfigDefinition value, out string error);
        bool TryGetLootByItemId(int itemId, out LootConfigDefinition value, out string error)
        {
            value = default;
            error = $"TbLoot.ItemId={itemId} 不存在";
            return false;
        }

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

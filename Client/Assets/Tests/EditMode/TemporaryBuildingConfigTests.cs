using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class TemporaryBuildingConfigTests
    {
        [Test]
        public void PlayerOwnsSharedBuildingReachAndProviderRejectsMissingReach()
        {
            var tables = LoadGeneratedTables();
            var provider = new LubanRuntimeConfigProvider(() => tables);
            Assert.That(provider.TryGetPlayer("player.graybox.default", out var player, out var error), Is.True, error);
            Assert.That(player.BuildingOperateDistance, Is.EqualTo(3f));
            SetReadonlyField(tables.TbPlayer.DataList[0], "BuildingOperateDistance", 0f);
            provider = new LubanRuntimeConfigProvider(() => tables);
            Assert.That(provider.TryGetPlayer("player.graybox.default", out _, out error), Is.False);
            Assert.That(error, Does.Contain("BuildingOperateDistance"));
        }

        [Test]
        public void GeneratedDefinitionsContainAllConfiguredKindsInOrder()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new Luban.ByteBuf(asset.bytes);
            }));
            Assert.That(provider.TryGetTempBuildings(out var buildings, out var error), Is.True, error);
            Assert.That(buildings.Where(value => !value.BaseOnly).Select(value => value.Id), Is.EqualTo(new[] { "temp_arrow_tower", "temp_roadblock_wood", "temp_campfire" }));
            Assert.That(buildings.Count(value => value.BaseOnly), Is.EqualTo(11));
            Assert.That(buildings[2].BlocksNavigation, Is.False);
            Assert.That(provider.TryGetTempBuildingManualAttack("temp_arrow_tower", out var attack), Is.True);
            Assert.That(attack.AmmoItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(provider.TryGetTempBuildingManualAttack("temp_roadblock_wood", out _), Is.False);
        }

        [Test]
        public void GeneratedDefinitionsMatchFixedValuesAndConfiguredPrefabAddresses()
        {
            var provider = new LubanRuntimeConfigProvider(LoadGeneratedTables);
            Assert.That(provider.TryGetTempBuildings(out var buildings, out var error), Is.True, error);
            Assert.That(buildings, Has.Count.EqualTo(14));

            AssertDefinition(buildings[0], "temp_arrow_tower", "固定箭塔", "temp_building.arrow_tower.name",
                "Prefabs/Building/GrayboxArrowTower", "Art/UI/TemporaryBuilding/GrayboxArrowTower", new[] { (ItemIds.MetalParts, 2), (ItemIds.Wood, 3) },
                1f, .75f, 20f, 1.2f, true, 100);
            AssertDefinition(buildings[1], "temp_roadblock_wood", "木质路障", "temp_building.roadblock_wood.name",
                "Prefabs/Building/GrayboxRoadblock", "Art/UI/TemporaryBuilding/GrayboxRoadblock", new[] { (ItemIds.Wood, 3) },
                1f, .9f, 20f, 1.2f, true, 200);
            AssertDefinition(buildings[2], "temp_campfire", "火堆", "temp_building.campfire.name",
                "Prefabs/Building/GrayboxCampfire", "Art/UI/TemporaryBuilding/GrayboxCampfire", new[] { (ItemIds.Wood, 2) },
                .5f, .6f, 20f, 0f, false, 300);

            Assert.That(provider.TryGetTempBuildingManualAttack("temp_arrow_tower", out var attack), Is.True);
            Assert.That(attack.InteractDistance, Is.EqualTo(2.2f));
            Assert.That(attack.ShootCooldownSeconds, Is.EqualTo(.8f));
            Assert.That(attack.Damage, Is.EqualTo(24));
            Assert.That(attack.ProjectileConfigId, Is.EqualTo("projectile.arrow.basic"));
            Assert.That(attack.AmmoItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(attack.AmmoAmount, Is.EqualTo(1));
            Assert.That(attack.MagazineCapacity, Is.EqualTo(5));
            Assert.That(attack.ReloadSeconds, Is.EqualTo(2f));
            Assert.That(attack.OperateRotateDegreesPerSecond, Is.EqualTo(180f));
            Assert.That(provider.TryGetTempBuildingManualAttack("temp_campfire", out _), Is.False);
        }

        [Test]
        public void ProviderRejectsInvalidBaseAndOrphanManualAttackRows()
        {
            var invalidTables = LoadGeneratedTables();
            SetReadonlyField(invalidTables.TbTempBuilding.DataList[0], "BuildSeconds", 0f);
            var provider = new LubanRuntimeConfigProvider(() => invalidTables);
            Assert.That(provider.TryGetTempBuildings(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("BuildSeconds"));

            var orphanTables = LoadGeneratedTables();
            SetReadonlyField(orphanTables.TbTempBuildingManualAttack.DataList[0], "BuildingId", "orphan.building");
            provider = new LubanRuntimeConfigProvider(() => orphanTables);
            Assert.That(provider.TryGetTempBuildings(out _, out error), Is.False);
            Assert.That(error, Does.Contain("引用不存在"));
        }

        [Test]
        public void YooAssetRuntimePrefabLocationsEnumerateEveryConfiguredBuildingAndSelectionUi()
        {
            var utilityType = Type.GetType("Game.Editor.YooAssetBuildUtility, Assembly-CSharp-Editor");
            Assert.That(utilityType, Is.Not.Null);
            var locations = ((System.Collections.IEnumerable)utilityType.GetProperty("RuntimePrefabLocations")?.GetValue(null))
                .Cast<string>()
                .ToArray();
            Assert.That(locations, Does.Contain("Prefabs/Building/GrayboxArrowTower"));
            Assert.That(locations, Does.Contain("Prefabs/Building/GrayboxRoadblock"));
            Assert.That(locations, Does.Contain("Prefabs/Building/GrayboxCampfire"));
            Assert.That(locations, Does.Contain("Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect"));
            Assert.That(locations.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(locations.Length));
        }

        [Test]
        public void BuildingPrefabsExposeCommonViewAndOptionalManualView()
        {
            var tower = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/GrayboxArrowTower.prefab");
            var roadblock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/GrayboxRoadblock.prefab");
            var campfire = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/GrayboxCampfire.prefab");
            Assert.That(tower, Is.Not.Null);
            Assert.That(roadblock, Is.Not.Null);
            Assert.That(campfire, Is.Not.Null);
            Assert.That(tower.GetComponent<TemporaryBuildingView>(), Is.Not.Null);
            var towerView = tower.GetComponent<TemporaryBuildingView>();
            var manualView = tower.GetComponent<TemporaryManualAttackView>();
            Assert.That(manualView, Is.Not.Null);
            Assert.That(manualView.InteractCollider, Is.Not.EqualTo(towerView.FootprintCollider));
            Assert.That(manualView.YawRoot, Is.Not.Null);
            Assert.That(manualView.Muzzle, Is.Not.Null);
            Assert.That(manualView.Muzzle, Is.Not.EqualTo(manualView.YawRoot));
            Assert.That(roadblock.GetComponent<TemporaryBuildingView>(), Is.Not.Null);
            Assert.That(roadblock.GetComponent<TemporaryManualAttackView>(), Is.Null);
            Assert.That(campfire.GetComponent<TemporaryBuildingView>(), Is.Not.Null);
            Assert.That(campfire.GetComponent<TemporaryManualAttackView>(), Is.Null);
        }

        [Test]
        public void CommittedBuildingViewsApplyConfiguredNavigationBlockingState()
        {
            AssertCommittedState("Assets/Game/Prefabs/Building/GrayboxArrowTower.prefab", true);
            AssertCommittedState("Assets/Game/Prefabs/Building/GrayboxRoadblock.prefab", true);
            AssertCommittedState("Assets/Game/Prefabs/Building/GrayboxCampfire.prefab", false);
        }

        [Test]
        public void CampfireVisualsStayHiddenDuringPreviewAndEnableAfterCommit()
        {
            const string prefabPath = "Assets/Game/Prefabs/Building/GrayboxCampfire.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            var prefabView = prefab.GetComponent<TemporaryBuildingView>();
            Assert.That(prefabView.CommittedEffectRoot, Is.Not.Null);
            Assert.That(prefabView.CommittedEffectRoot.activeSelf, Is.False);

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<TemporaryBuildingView>();
                var effectRoot = view.CommittedEffectRoot;
                var effect = effectRoot.GetComponent<CampfireVisualEffect>();
                var fireLight = effectRoot.GetComponent<Light>();
                var flameParticles = effectRoot.GetComponent<ParticleSystem>();

                Assert.That(effect, Is.Not.Null);
                Assert.That(effect.ValidateReferences(out var error), Is.True, error);
                Assert.That(fireLight, Is.Not.Null);
                Assert.That(fireLight.type, Is.EqualTo(LightType.Point));
                Assert.That(fireLight.color.r, Is.EqualTo(1f).Within(.001f));
                Assert.That(fireLight.color.g, Is.EqualTo(.36f).Within(.001f));
                Assert.That(fireLight.color.b, Is.EqualTo(.08f).Within(.001f));
                Assert.That(fireLight.intensity, Is.EqualTo(2.2f).Within(.001f));
                Assert.That(fireLight.range, Is.EqualTo(6f).Within(.001f));
                Assert.That(fireLight.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(flameParticles, Is.Not.Null);

                view.SetPreviewValidity(true);
                Assert.That(effectRoot.activeSelf, Is.False);
                Assert.That(view.SetCommitted(false, out error), Is.True, error);
                Assert.That(effectRoot.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Tables LoadGeneratedTables()
        {
            return new Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new Luban.ByteBuf(asset.bytes);
            });
        }

        private static void SetReadonlyField<T>(T target, string name, object value)
        {
            var field = typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static void AssertCommittedState(string prefabPath, bool blocksNavigation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<TemporaryBuildingView>();
                Assert.That(view, Is.Not.Null, prefabPath);
                Assert.That(view.SetCommitted(blocksNavigation, out var error), Is.True, error);
                Assert.That(view.FootprintCollider.isTrigger, Is.EqualTo(!blocksNavigation));
                if (blocksNavigation)
                {
                    Assert.That(view.NavMeshObstacle, Is.Not.Null, prefabPath);
                    Assert.That(view.NavMeshObstacle.enabled, Is.True);
                    Assert.That(view.NavMeshObstacle.carving, Is.True);
                    Assert.That(view.NavMeshObstacle.carveOnlyStationary, Is.True);
                }
                else if (view.NavMeshObstacle != null)
                {
                    Assert.That(view.NavMeshObstacle.enabled, Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertDefinition(
            TempBuildingDefinition definition,
            string id,
            string name,
            string nameKey,
            string prefabAddress,
            string iconAddress,
            IReadOnlyList<(int itemId, int amount)> materials,
            float buildSeconds,
            float footprintRadius,
            float maxSlopeDegrees,
            float narrowPassageRadius,
            bool blocksNavigation,
            int order)
        {
            Assert.That(definition.Id, Is.EqualTo(id));
            Assert.That(definition.Name, Is.EqualTo(name));
            Assert.That(definition.NameKey, Is.EqualTo(nameKey));
            Assert.That(definition.IconAddress, Is.EqualTo(iconAddress));
            Assert.That(definition.PrefabAddress, Is.EqualTo(prefabAddress));
            Assert.That(definition.Materials.Select(value => (value.ItemId, value.Amount)), Is.EqualTo(materials));
            Assert.That(definition.BuildSeconds, Is.EqualTo(buildSeconds));
            Assert.That(definition.FootprintRadius, Is.EqualTo(footprintRadius));
            Assert.That(definition.MaxSlopeDegrees, Is.EqualTo(maxSlopeDegrees));
            Assert.That(definition.NarrowPassageRadius, Is.EqualTo(narrowPassageRadius));
            Assert.That(definition.BlocksNavigation, Is.EqualTo(blocksNavigation));
            Assert.That(definition.Order, Is.EqualTo(order));
        }

    }
}

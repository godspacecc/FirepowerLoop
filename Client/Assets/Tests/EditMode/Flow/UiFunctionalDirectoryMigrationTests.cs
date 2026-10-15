using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class UiFunctionalDirectoryMigrationTests
    {
        private readonly struct AssetEntry
        {
            public AssetEntry(string path, string guid, string oldPath, string view = null)
            {
                Path = path; Guid = guid; OldPath = oldPath; View = view;
            }
            public string Path { get; }
            public string Guid { get; }
            public string OldPath { get; }
            public string View { get; }
        }

        // 固定为迁移前记录的 GUID；测试不得从当前 AssetDatabase、.meta 或扫描结果推导期望值。
        private static readonly AssetEntry[] Prefabs =
        {
            new AssetEntry("Assets/Game/Prefabs/UI/Inventory/Bag.prefab","4d7785bf38fdd4a4681954901ee50e95",null,"BagUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Inventory/Warehouse.prefab","8f112d4b0dd7e4844bf20f64f4fc8dc5",null,"WarehouseUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab","0ddf9523493916c478f014d5992791d4",null,"SearchContainerUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Craft/Craft.prefab","a395e0e4d027b2d4482b48a23365f095","Assets/Game/Prefabs/UI/Common/Craft.prefab","CraftUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab","0ccc66ff5220c4d4e8e1fc8075adf573","Assets/Game/Prefabs/UI/Common/ExpeditionHud.prefab","ExpeditionHudUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab","13f9f4fb02ea3614594b9d511a18503c","Assets/Game/Prefabs/UI/Common/PlayerConditionHud.prefab","PlayerConditionHudUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingHud.prefab","1120ea4f6e50f8149b28b6f5dd7ae5f9","Assets/Game/Prefabs/UI/Common/TemporaryBuildingHud.prefab","TemporaryBuildingHudUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab","5a4e6d3cbfba48958e0c0c34c74f1b23","Assets/Game/Prefabs/UI/Common/TemporaryBuildingSelect.prefab","TemporaryBuildingSelectUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab","de5223b045130984fa4d2f53794b1749","Assets/Game/Prefabs/UI/Common/RuntimeLog.prefab","RuntimeLogUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab","3c6c3ac513d9dbf4eb1ee05827647a97","Assets/Game/Prefabs/UI/Common/TestCmd.prefab","TestCmdUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/TestCmdButton.prefab","889a13e3200dfd645bad25e8c9ee7c13","Assets/Game/Prefabs/UI/Common/TestCmdButton.prefab","TestCmdButtonUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Inventory/Box/InventorySlotBox.prefab","231e7fe35a1e13a4fb881df79bf0c06c","Assets/Game/Prefabs/UI/Common/Box/InventorySlotBox.prefab","InventorySlotBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Craft/Box/CraftCategoryBox.prefab","00b8b6ad1c2a4b54dad10fa6b6d7b841","Assets/Game/Prefabs/UI/Common/Box/CraftCategoryBox.prefab","CraftCategoryBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Craft/Box/CraftMaterialBox.prefab","86f9687a80be03749b98dda09b9da133","Assets/Game/Prefabs/UI/Common/Box/CraftMaterialBox.prefab","CraftMaterialBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Craft/Box/CraftRecipeBox.prefab","1fa4758c0581bc14aad40fa8822545ce","Assets/Game/Prefabs/UI/Common/Box/CraftRecipeBox.prefab","CraftRecipeBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/TemporaryBuilding/Box/TemporaryBuildingOptionBox.prefab","5a4e6d3cbfba48958e0c0c34c74f1b22","Assets/Game/Prefabs/UI/Common/Box/TemporaryBuildingOptionBox.prefab","TemporaryBuildingOptionBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/Box/RuntimeLogItemBox.prefab","a04661945b1ca7840b03bce84fa808ad","Assets/Game/Prefabs/UI/Common/Box/RuntimeLogItemBox.prefab","RuntimeLogItemBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdCategoryBox.prefab","2ec4168844fa61748a8869db21f7b86d","Assets/Game/Prefabs/UI/Common/Box/TestCmdCategoryBox.prefab","TestCmdCategoryBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdEntryBox.prefab","b198bcaac4766e445bdc2ad036e21654","Assets/Game/Prefabs/UI/Common/Box/TestCmdEntryBox.prefab","TestCmdEntryBoxUI")
        };

        private static readonly AssetEntry[] OtherAssets = ParseEntries(@"
Assets/Game/Scripts/Controller/UI/Inventory/BagUI.cs|d7de1ba7975e6e4499550053589372b8|
Assets/Game/Scripts/Controller/UI/Inventory/WarehouseUI.cs|35593b7bf16cd2d4f9e236c3387468df|
Assets/Game/Scripts/Controller/UI/Inventory/SearchContainerUI.cs|3aafc948e01fc2f479a84ee42a952842|
Assets/Game/Scripts/Controller/UI/Craft/CraftUI.cs|069f0d86620bd1743b151a67c4c50a68|Assets/Game/Scripts/Controller/UI/Common/CraftUI.cs
Assets/Game/Scripts/Controller/UI/Expedition/ExpeditionHudUI.cs|e4b1995f5c1b4ac458a761ae4a4e5bea|Assets/Game/Scripts/Controller/UI/Common/ExpeditionHudUI.cs
Assets/Game/Scripts/Controller/UI/Player/PlayerConditionHudUI.cs|903c3f7189784764383452795c07ce7f|Assets/Game/Scripts/Controller/UI/Common/PlayerConditionHudUI.cs
Assets/Game/Scripts/Controller/UI/TemporaryBuilding/TemporaryBuildingHudUI.cs|99210ff6f2b413b4d8c7498d5f981ccb|Assets/Game/Scripts/Controller/UI/Common/TemporaryBuildingHudUI.cs
Assets/Game/Scripts/Controller/UI/TemporaryBuilding/TemporaryBuildingSelectUI.cs|832ea5f43d9841b7a7fae48b93ae0061|Assets/Game/Scripts/Controller/UI/Common/TemporaryBuildingSelectUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/RuntimeLogUI.cs|6bccda9ff000f29438b8ef0e1aaa1d24|Assets/Game/Scripts/Controller/UI/Common/RuntimeLogUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/TestCmdUI.cs|1a2d3f4cd2c8eed4ca49105a566f78f6|Assets/Game/Scripts/Controller/UI/Common/TestCmdUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/TestCmdButtonUI.cs|3609bfa8d3bef1b41a14dea427eca73b|Assets/Game/Scripts/Controller/UI/Common/TestCmdButtonUI.cs
Assets/Game/Scripts/Controller/UI/Inventory/Box/InventorySlotBoxUI.cs|f2694a9155d23b843b8a549c9b9423b1|Assets/Game/Scripts/Controller/UI/Common/Box/InventorySlotBoxUI.cs
Assets/Game/Scripts/Controller/UI/Craft/Box/CraftCategoryBoxUI.cs|e9aefd447ee511949872749ca860eaf8|Assets/Game/Scripts/Controller/UI/Common/Box/CraftCategoryBoxUI.cs
Assets/Game/Scripts/Controller/UI/Craft/Box/CraftMaterialBoxUI.cs|4f3f5e2a07cdea04182d912898cf1874|Assets/Game/Scripts/Controller/UI/Common/Box/CraftMaterialBoxUI.cs
Assets/Game/Scripts/Controller/UI/Craft/Box/CraftRecipeBoxUI.cs|41adaa9d4b3b0d7439ed70668c17e47d|Assets/Game/Scripts/Controller/UI/Common/Box/CraftRecipeBoxUI.cs
Assets/Game/Scripts/Controller/UI/TemporaryBuilding/Box/TemporaryBuildingOptionBoxUI.cs|2fe9bf1ad3ca4010b4cfdc27eca98ab7|Assets/Game/Scripts/Controller/UI/Common/Box/TemporaryBuildingOptionBoxUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/Box/RuntimeLogItemBoxUI.cs|ffe9984e15e08d94da61485dc9af1197|Assets/Game/Scripts/Controller/UI/Common/Box/RuntimeLogItemBoxUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/Box/TestCmdCategoryBoxUI.cs|9490759637b7e1a4f991e0fddacba7f5|Assets/Game/Scripts/Controller/UI/Common/Box/TestCmdCategoryBoxUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/Box/TestCmdEntryBoxUI.cs|b51e1ed8eef9f284eb8a61b469836ff6|Assets/Game/Scripts/Controller/UI/Common/Box/TestCmdEntryBoxUI.cs
Assets/Game/Scripts/Controller/UI/Inventory/Box/EquipmentSlotBoxUI.cs|f3348b03414082d4094e7b5a97300f71|Assets/Game/Scripts/Controller/UI/Common/Box/EquipmentSlotBoxUI.cs
Assets/Game/Scripts/Controller/UI/Home/RedPointView.cs|d1681b0156a6c4a4a80cccda959bb169|Assets/Game/Scripts/Controller/UI/Common/RedPointView.cs
Assets/Game/Scripts/Controller/UICtrl/Inventory/BagUICtrl.cs|c5cf8a4b9a4303343b319ba2f3439f57|
Assets/Game/Scripts/Controller/UICtrl/Inventory/WarehouseUICtrl.cs|08ca36c1172d9ea4a91e5dab348814e4|
Assets/Game/Scripts/Controller/UICtrl/Inventory/SearchContainerUICtrl.cs|ef76836dd9ffdcf4c8bbe3f8d3704b13|
Assets/Game/Scripts/Controller/UICtrl/Craft/CraftUICtrl.cs|f4db1439b5350a44589603e078c03588|Assets/Game/Scripts/Controller/UICtrl/Common/CraftUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Expedition/ExpeditionHudUICtrl.cs|d79a2c3597031114b963ef09fe9c5b7c|Assets/Game/Scripts/Controller/UICtrl/Common/ExpeditionHudUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Player/PlayerConditionHudUICtrl.cs|5024ec427659871429854f7c7fcefea7|Assets/Game/Scripts/Controller/UICtrl/Common/PlayerConditionHudUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/TemporaryBuilding/TemporaryBuildingHudUICtrl.cs|c3c826f193a11294e99aa5d7a68ffbd1|Assets/Game/Scripts/Controller/UICtrl/Common/TemporaryBuildingHudUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/TemporaryBuilding/TemporaryBuildingSelectUICtrl.cs|f6cf349eed684d10ab245fedf0d39bfc|Assets/Game/Scripts/Controller/UICtrl/Common/TemporaryBuildingSelectUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Diagnostics/RuntimeLogUICtrl.cs|fe1ce3f15e6953f418a89bfee7905734|Assets/Game/Scripts/Controller/UICtrl/Common/RuntimeLogUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Diagnostics/TestCmdUICtrl.cs|1038abc2e6731ea4d83c7f415cc4bcf2|Assets/Game/Scripts/Controller/UICtrl/Common/TestCmdUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Diagnostics/TestCmdButtonUICtrl.cs|f0be32e26dd46a4478cd3c23e4aa3c2a|Assets/Game/Scripts/Controller/UICtrl/Common/TestCmdButtonUICtrl.cs");

        private static readonly string[] RuntimeLocations = ParseLines(@"
Prefabs/Characters/GrayboxEnemy
Prefabs/Characters/GrayboxPlayer
Prefabs/Combat/GrayboxArrow
Prefabs/Building/GrayboxArrowTower
Prefabs/Building/GrayboxCampfire
Prefabs/Building/GrayboxRoadblock
Prefabs/SceneObjects/TaskExit
Prefabs/SceneObjects/TestResourcePickup
Prefabs/UI/Common/Loading
Prefabs/UI/Diagnostics/RuntimeLog
Prefabs/UI/Diagnostics/TestCmd
Prefabs/UI/Expedition/ExpeditionHud
Prefabs/UI/Home/Setting
Prefabs/UI/Home/Start
Prefabs/UI/Inventory/Bag
Prefabs/UI/Inventory/Warehouse
Prefabs/UI/Inventory/SearchContainer
Prefabs/UI/Notify/Float
Prefabs/UI/Notify/Message
Prefabs/UI/Player/PlayerConditionHud
Prefabs/UI/Player/GameplayGuideHud
Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect");

        [Test]
        public void FixedMigrationTableKeepsAllFiftyOneGuidsAndRemovesOldPaths()
        {
            foreach (var asset in Prefabs.Concat(OtherAssets))
                AssertAsset(asset);
            Assert.That(Prefabs.Length + OtherAssets.Length, Is.EqualTo(51));
        }

        [Test]
        public void NineteenPrefabsHaveExactRootCollectorAndGeneratedViewBindings()
        {
            foreach (var asset in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset.Path);
                var collectors = prefab.GetComponents<ReferenceCollector>();
                Assert.That(collectors, Has.Length.EqualTo(1), asset.Path + ": root collector");
                var views = prefab.GetComponents<MonoBehaviour>().Where(item => item.GetType().Name == asset.View).ToArray();
                Assert.That(views, Has.Length.EqualTo(1), asset.Path + ": generated view");
                var fields = views[0].GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                IEnumerable<ReferenceCollectorData> data = collectors[0].data ?? new List<ReferenceCollectorData>();
                Assert.That(data.Select(item => item.key), Is.Unique, asset.Path + ": duplicate collector key");
                Assert.That(data.Select(item => item.key), Is.EquivalentTo(fields.Select(item => item.Name)), asset.Path + ": key/field set");
                foreach (var field in fields)
                {
                    var item = data.Single(candidate => candidate.key == field.Name);
                    var source = item.gameObject;
                    Assert.That(source, Is.Not.Null, asset.Path + ": " + field.Name + " reference");
                    var expected = CollectorTarget(source, field.FieldType, asset.Path, field.Name);
                    Assert.That(
                        item.TypeName == field.FieldType.FullName || item.TypeName == expected.GetType().FullName,
                        Is.True,
                        asset.Path + ": " + field.Name + " type");
                    Assert.That(field.GetValue(views[0]), Is.SameAs(expected), asset.Path + ": " + field.Name + " binding");
                }
            }
            var button = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/TestCmdButton.prefab");
            Assert.That(button.GetComponents<TestCmdButtonPointerRelay>(), Has.Length.EqualTo(1));
            Assert.That(button.GetComponent<ReferenceCollector>().data.Select(item => item.key), Is.EquivalentTo(new[] { "Button", "TextTitle" }));
        }

        [Test]
        public void CommonRedPointAndYooAssetGuardsAreExact()
        {
            AssertDirectFiles("Game/Prefabs/UI/Common", "Loading.prefab", "Loading.prefab.meta");
            AssertDirectFiles("Game/Scripts/Controller/UI/Common", "LoadingUI.cs", "LoadingUI.cs.meta");
            AssertDirectFiles("Game/Scripts/Controller/UICtrl/Common", "LoadingUICtrl.cs", "LoadingUICtrl.cs.meta");
            Assert.That(AssetDatabase.IsValidFolder("Assets/Game/Prefabs/UI/Common/Box"), Is.False);
            Assert.That(AssetDatabase.IsValidFolder("Assets/Game/Scripts/Controller/UI/Common/Box"), Is.False);
            AssertAsset(OtherAssets.Single(item => item.Path.EndsWith("RedPointView.cs", StringComparison.Ordinal)));
            var start = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            Assert.That(start.GetComponent<StartUI>().UpdateRedPoint, Is.TypeOf<RedPointView>());

            var utility = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.Editor.YooAssetBuildUtility", false)).FirstOrDefault(t => t != null);
            Assert.That(utility, Is.Not.Null);
            var locations = ((IEnumerable<string>)utility.GetProperty("RuntimePrefabLocations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null)).ToArray();
            Assert.That(locations.OrderBy(item => item, StringComparer.Ordinal), Is.EqualTo(RuntimeLocations.OrderBy(item => item, StringComparer.Ordinal)));
            foreach (var oldLocation in new[] { "Prefabs/UI/Common/ExpeditionHud", "Prefabs/UI/Common/PlayerConditionHud", "Prefabs/UI/Common/RuntimeLog", "Prefabs/UI/Common/TemporaryBuildingSelect", "Prefabs/UI/Common/TestCmd" })
                Assert.That(locations, Does.Not.Contain(oldLocation));
            object[] arguments = { null };
            var valid = (bool)utility.GetMethod("ValidateRequiredRuntimePrefabSources", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, arguments);
            Assert.That(valid, Is.True, arguments[0] as string);
            Assert.That(arguments[0], Is.Null.Or.Empty);
        }

        private static void AssertAsset(AssetEntry asset)
        {
            Assert.That(AssetDatabase.LoadMainAssetAtPath(asset.Path), Is.Not.Null, asset.Path);
            Assert.That(AssetDatabase.AssetPathToGUID(asset.Path), Is.EqualTo(asset.Guid), asset.Path);
            if (!string.IsNullOrWhiteSpace(asset.OldPath))
                Assert.That(AssetDatabase.LoadMainAssetAtPath(asset.OldPath), Is.Null, asset.OldPath);
        }

        private static UnityEngine.Object CollectorTarget(UnityEngine.Object source, Type type, string path, string field)
        {
            if (!typeof(Component).IsAssignableFrom(type))
                return source as GameObject;
            if (source is Component component)
            {
                Assert.That(type.IsInstanceOfType(component), Is.True, path + ": " + field + " component type");
                Assert.That(component.gameObject.GetComponents(type), Is.EqualTo(new[] { component }), path + ": " + field + " unique component");
                return component;
            }
            var gameObject = source as GameObject;
            Assert.That(gameObject, Is.Not.Null, path + ": " + field + " GameObject source");
            var components = gameObject.GetComponents(type);
            Assert.That(components, Has.Length.EqualTo(1), path + ": " + field + " unique component");
            return components[0];
        }

        private static void AssertDirectFiles(string relativeDirectory, params string[] expected)
        {
            var directory = Path.Combine(Application.dataPath, relativeDirectory);
            Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName), Is.EquivalentTo(expected), relativeDirectory);
        }

        private static AssetEntry[] ParseEntries(string text)
        {
            return ParseLines(text).Select(line => line.Split('|')).Select(item => new AssetEntry(item[0], item[1], item[2])).ToArray();
        }

        private static string[] ParseLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

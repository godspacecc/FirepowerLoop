using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ItemContextUiTests
    {
        [Test]
        public void ItemContextPrefabsUseGeneratedBindingsAndExternalListItems()
        {
            var menu = Load<ItemContextMenuUI>("Assets/Game/Prefabs/UI/Inventory/ItemContextMenu.prefab");
            var tooltip = Load<ItemTooltipUI>("Assets/Game/Prefabs/UI/Inventory/ItemTooltip.prefab");
            var action = Load<ItemContextActionBoxUI>("Assets/Game/Prefabs/UI/Inventory/Box/ItemContextActionBox.prefab");

            Assert.That(menu.PanelRect, Is.Not.Null);
            Assert.That(menu.ActionList.itemPrefab, Is.EqualTo(action.gameObject));
            Assert.That(tooltip.IconImage, Is.Not.Null);
            Assert.That(tooltip.NameText, Is.Not.Null);
            Assert.That(tooltip.DescriptionText, Is.Not.Null);
            Assert.That(action.BtnAction, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(menu.ActionList.itemPrefab), Is.True);
        }

        [Test]
        public void ItemContextPrefabsContainNoMissingScripts()
        {
            foreach (var path in new[]
                     {
                         "Assets/Game/Prefabs/UI/Inventory/ItemContextMenu.prefab",
                         "Assets/Game/Prefabs/UI/Inventory/ItemTooltip.prefab",
                         "Assets/Game/Prefabs/UI/Inventory/Box/ItemContextActionBox.prefab"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                    .Sum(node => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject)), Is.Zero, path);
            }
        }

        private static T Load<T>(string path) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var component = prefab.GetComponent<T>();
            Assert.That(component, Is.Not.Null, path);
            return component;
        }
    }
}

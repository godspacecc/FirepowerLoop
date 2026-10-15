using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BasePreparationPrefabTests
    {
        private void References(Component view)
        {Assert.That(view,Is.Not.Null);foreach(var field in view.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))if(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))Assert.That(field.GetValue(view) as UnityEngine.Object,Is.Not.Null,view.GetType().Name+"."+field.Name);}
        [Test] public void StatePageAndNestedBagHaveDeserializedGeneratedReferences()
        {
            var page=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/StatusManagement.prefab");References(page.GetComponent<StatusManagementPageUI>());
            var bag=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");References(bag.GetComponent<BagUI>().StatusPage);
        }
        [Test] public void MapForbiddenListOwnsExternalBoxAndFixedLabelsAreReadable()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab");var view=root.GetComponent<MapSelectionUI>();References(view);
            Assert.That(AssetDatabase.GetAssetPath(view.ForbiddenList.itemPrefab),Is.EqualTo("Assets/Game/Prefabs/UI/Inventory/Box/PreparationForbiddenItem.prefab"));References(view.ForbiddenList.itemPrefab.GetComponent<PreparationForbiddenItemBoxUI>());
            var texts=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"Game/Config/Localization/zh-CN.json")));
            foreach(var label in root.GetComponentsInChildren<LocalizedText>(true))
                if(label.Key.StartsWith("ui.base_prep.",StringComparison.Ordinal))Assert.That(label.GetComponent<TMPro.TextMeshProUGUI>().text,Is.EqualTo(texts[label.Key].Value<string>()));
        }
    }
}

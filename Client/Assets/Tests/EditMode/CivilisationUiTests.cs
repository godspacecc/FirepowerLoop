using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    public sealed class CivilisationUiTests
    {
        private CivilisationProductionTests support;
        [SetUp] public void SetUp() { support = new CivilisationProductionTests(); support.SetUp(); }
        [TearDown] public void TearDown() => support.TearDown();
        private static JObject Receipt() => new JObject
        {
            ["expeditionId"] = "0123456789abcdef0123456789abcdef", ["acknowledged"] = false, ["gold"] = 4,
            ["newTechnologies"] = new JArray("technology.dry-ration"), ["visited"] = new JArray("camp.fire"),
            ["rows"] = new JArray(new JObject { ["itemId"] = 29, ["amount"] = 2, ["excluded"] = 0, ["gold"] = 4 })
        };
        [Test] public void ExpeditionEntryExplainsSearchGoalAndAutomaticConversion()
        {
            Assert.That(MapCatalog.TryGet(MapIds.CivilisationValley, out var definition), Is.True);
            var method = typeof(MapSelectionUICtrl).GetMethod("TryFormatTaskDetail", BindingFlags.Instance | BindingFlags.NonPublic);
            var arguments = new object[] { definition, null };
            Assert.That((bool)method.Invoke(new MapSelectionUICtrl(), arguments), Is.True);
            Assert.That((string)arguments[1], Does.Contain("工艺").And.Contain("自动兑换").And.Contain("装备"));
        }
        [Test] public void ValleyInteractablesHaveSerializedReadableTargetPrompts()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/CivilisationValleyMap.unity", OpenSceneMode.Additive);
            try
            {
                var count = 0;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var view in root.GetComponentsInChildren<CivilisationPointView>(true))
                {
                    count++;
                    var serialized = new SerializedObject(view);
                    var prompt = serialized.FindProperty("nameRoot").objectReferenceValue as GameObject;
                    Assert.That(prompt, Is.Not.Null, view.name + " 缺少交互名称 HUD");
                    var text = prompt.GetComponentInChildren<TMPro.TMP_Text>(true);
                    Assert.That(text, Is.Not.Null, view.name);
                    Assert.That(text.text, Is.Not.Empty.And.Not.StartsWith("civilisation."));
                    Assert.That(prompt.activeSelf, Is.False, "未聚焦时名称应隐藏");
                    Assert.That(prompt.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
                    Assert.That(text.GetComponent<LocalizedText>(), Is.Not.Null);
                }
                Assert.That(count, Is.EqualTo(19));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        [Test] public void ReceiptTextShowsTechnologyConversionAndPlacesWithoutPayingRewards()
        {
            var method = typeof(CivilisationSettlementUICtrl).GetMethod("BuildReceiptText", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "正式结算呈现尚未接入");
            var text = (string)method.Invoke(null, new object[] { Receipt(), Game.Interface.GetUtility<IRuntimeConfigProvider>() });
            Assert.That(text, Does.Contain("干粮制作").And.Contain("采集食物").And.Contain("旧火塘").And.Contain("4"));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(8));
        }
        [Test] public void BothFormalPrefabsHaveGeneratedViewsAndValidCollectedReferences()
        {
            foreach (var name in new[] { "CivilisationWorkshop", "CivilisationSettlement" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/UI/Civilisation/{name}.prefab");
                Assert.That(prefab, Is.Not.Null);
                var view = prefab.GetComponent<BaseUI>(); Assert.That(view, Is.Not.Null);
                Assert.That(view.GetType().Name, Is.EqualTo(name + "UI"));
                var collector = prefab.GetComponent<ReferenceCollector>(); Assert.That(collector, Is.Not.Null);
                foreach (var entry in collector.data)
                {
                    Assert.That(entry.gameObject, Is.Not.Null, name + "." + entry.key);
                    var field = view.GetType().GetField(entry.key); Assert.That(field, Is.Not.Null, entry.key);
                    Assert.That(field.GetValue(view), Is.EqualTo(entry.gameObject), entry.key);
                }
            }
        }
        [Test] public void ReceiptAcknowledgementIsIdempotentAndCannotPayAgain()
        {
            var model = Game.Interface.GetModel<CivilisationTechnologyModel>();
            model.Restore(new JObject { ["schemaVersion"] = 1, ["unlocked"] = new JArray("technology.dry-ration"), ["receipt"] = Receipt() });
            var system = Game.Interface.GetSystem<CivilisationProductionSystem>();
            Assert.That(system.TryAcknowledgeReceipt(out var error), Is.True, error);
            Assert.That(system.TryAcknowledgeReceipt(out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(8));
            Assert.That(model.Receipt.Value<bool>("acknowledged"), Is.True);
        }
    }
}

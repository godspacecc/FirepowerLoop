using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class HistoricalUiLocalizationOwnershipTests
    {
        private string tempRoot;

        private static readonly IReadOnlyDictionary<string, FixedTextExpectation> StartFixedKeys =
            new Dictionary<string, FixedTextExpectation>
            {
                { "Panel/TitleText", Fixed("ui.start.title", "ANOTHER\nDAY") },
                { "Panel/ButtonGroup/BtnNewGame/Text", Fixed("ui.start.new_game", "新游戏") },
                { "Panel/ButtonGroup/BtnContinue/Text", Fixed("ui.start.continue", "继续游戏") },
                { "Panel/ButtonGroup/BtnOnline/BtnOnlineText", Fixed("ui.start.online", "联机游戏") },
                { "BtnCheckUpdate/Text", Fixed("ui.start.check_update", "检查更新") },
                { "Panel/ButtonGroup/BtnSetting/Text", Fixed("ui.start.settings", "设置") },
                { "BtnExit/Text", Fixed("ui.start.exit", "退出游戏") }
            };

        private static readonly IReadOnlyDictionary<string, FixedTextExpectation> LoadingFixedKeys =
            new Dictionary<string, FixedTextExpectation>
            {
                { "Title", Fixed("ui.loading.title", "加载中") },
                { "BaseTitle", Fixed("ui.loading.enter_base", "进入基地") },
                { "ErrorRoot/BackButton/Text", Fixed("ui.common.back", "返回") }
            };

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            GameObjectPoolHelper.ClearForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayHistoricalUiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            GameObjectPoolHelper.ClearForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            DestroySceneObject("UIRoot");
            DestroySceneObject("EventSystem");
            foreach (var root in Object.FindObjectsOfType<Transform>())
            {
                if (root.parent == null && root.name.EndsWith("(Clone)", StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(root.gameObject);
                }
            }

            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void HistoricalPrefabs_OwnFixedTextsWithLocalizedTextAndExposeOnlyDynamicFields()
        {
            AssertPrefabContract<StartUI>(
                "Assets/Game/Prefabs/UI/Home/Start.prefab",
                StartFixedKeys,
                new[]
                {
                    "TitleText", "BtnNewGameText", "BtnContinueText",
                    "BtnOnlineText", "BtnCheckUpdateText", "BtnSettingText", "BtnExitText"
                },
                new[] { "ButtonGroup", "VersionText", "UpdateStatusText" });

            AssertPrefabContract<LoadingUI>(
                "Assets/Game/Prefabs/UI/Common/Loading.prefab",
                LoadingFixedKeys,
                new[] { "TitleText" },
                new[] { "MessageText", "ProgressSlider", "PercentText", "ErrorRoot", "ErrorText", "BtnBack" });

            var messageButton = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Notify/Box/MessageButtonBox.prefab");
            var buttonText = messageButton.transform.Find("ButtonText").GetComponent<TextMeshProUGUI>();
            Assert.That(buttonText.GetComponents<LocalizedText>(), Is.Empty);
            Assert.That(typeof(MessageButtonBoxUI).GetField("ButtonText"), Is.Not.Null);
        }

        [Test]
        public void LocalizedTextRefreshesFixedTextsAndLeavesDynamicTextsUntouchedOnLanguageEvent()
        {
            SetupLocalizationHarness();

            var start = Object.Instantiate(Load("Assets/Game/Prefabs/UI/Home/Start.prefab"));
            var startUi = start.GetComponent<StartUI>();
            startUi.UpdateStatusText.text = "dynamic update";
            Assert.That(Text(start, "Panel/TitleText").text, Is.EqualTo("开始"));

            var loading = Object.Instantiate(Load("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            var loadingUi = loading.GetComponent<LoadingUI>();
            loadingUi.MessageText.text = "dynamic loading";
            Assert.That(Text(loading, "Title").text, Is.EqualTo("加载中"));

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));

            Assert.That(Text(start, "Panel/TitleText").text, Is.EqualTo("Start"));
            Assert.That(startUi.UpdateStatusText.text, Is.EqualTo("dynamic update"));
            Assert.That(Text(loading, "Title").text, Is.EqualTo("Loading"));
            Assert.That(loadingUi.MessageText.text, Is.EqualTo("dynamic loading"));
        }

        [Test]
        public void MessageButtonBoxKeepsDynamicTextThroughMessageControllerAcrossReuse()
        {
            SetupControllerHarness();

            var message = UIHelper.ShowUI<MessageUICtrl>();
            message.SetConfig(Message("A"));
            var ui = GetBaseUi<MessageUI>(message);
            AssertSingleButton(ui, "A");
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            AssertSingleButton(ui, "A");

            message.SetConfig(Message("B"));
            AssertSingleButton(ui, "B");
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("zh-CN");
            Game.Interface.SendEvent(new LanguageChangedEvent("en-US", "zh-CN"));
            AssertSingleButton(ui, "B");

            message.SetConfig(Message("A"));
            AssertSingleButton(ui, "A");
        }

        private static void AssertPrefabContract<T>(
            string prefabPath,
            IReadOnlyDictionary<string, FixedTextExpectation> fixedKeys,
            string[] removedFields,
            string[] dynamicFields)
            where T : BaseUI
        {
            var prefab = Load(prefabPath);
            var collector = prefab.GetComponent<ReferenceCollector>();
            var fields = typeof(T)
                .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(field => field.Name);

            foreach (var field in removedFields)
            {
                Assert.That(fields.ContainsKey(field), Is.False, field);
                Assert.That(collector.data.Any(item => item.key == field), Is.False, field);
            }

            foreach (var field in dynamicFields)
            {
                Assert.That(fields.ContainsKey(field), Is.True, field);
                Assert.That(collector.data.Any(item => item.key == field), Is.True, field);
            }

            foreach (var fixedKey in fixedKeys)
            {
                var text = Text(prefab, fixedKey.Key);
                Assert.That(text.text, Is.EqualTo(fixedKey.Value.DefaultText), fixedKey.Key);
                var components = text.GetComponents<LocalizedText>();
                Assert.That(components.Length, Is.EqualTo(1), fixedKey.Key);
                Assert.That(components[0].Key, Is.EqualTo(fixedKey.Value.Key), fixedKey.Key);
                Assert.That(components[0].Texts, Is.Empty, fixedKey.Key);
            }
        }

        private static FixedTextExpectation Fixed(string key, string defaultText)
        {
            return new FixedTextExpectation(key, defaultText);
        }

        private static void SetupLocalizationHarness()
        {
            ResourceLoader.SetLoaderForTests(new HistoricalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            var architecture = new Game();
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            typeof(Architecture<Game>)
                .GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(architecture, true);

            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility(new LocalizationUtility());
        }

        private void SetupControllerHarness()
        {
            SetupLocalizationHarness();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            CreateUIRoot();
            UIRoot.EnsureInstance();
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static TextMeshProUGUI Text(GameObject root, string path)
        {
            var transform = root.transform.Find(path);
            Assert.That(transform, Is.Not.Null, path);
            return transform.GetComponent<TextMeshProUGUI>();
        }

        private static T GetBaseUi<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static MessageConfig Message(string text)
        {
            return new MessageConfig
            {
                Title = "Title",
                Content = "Content",
                Buttons = new List<MessageButtonConfig>
                {
                    new MessageButtonConfig { ButtonText = text }
                }
            };
        }

        private static void AssertSingleButton(MessageUI ui, string text)
        {
            var buttons = ui.ButtonRoot.GetComponentsInChildren<MessageButtonBoxUI>(false);
            Assert.That(buttons.Length, Is.EqualTo(1));
            Assert.That(buttons[0].ButtonText.GetComponents<LocalizedText>(), Is.Empty);
            Assert.That(buttons[0].ButtonText.text, Is.EqualTo(text));
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.AddComponent<UIRoot>();
            CreateRect(root.transform, "HUD");
            CreateRect(root.transform, "Dialog");
            CreateRect(root.transform, "Popup");
            CreateRect(root.transform, "Tips");
            CreateRect(root.transform, "Top");
            var eventSystem = new GameObject("EventSystem", typeof(RectTransform));
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null)
            {
                eventSystem.AddComponent(inputModuleType);
            }
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void DestroySceneObject(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class HistoricalLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset(
                            "{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                        return new TextAsset(
                            "{\"ui.start.title\":\"开始\",\"ui.start.new_game\":\"新游戏\",\"ui.start.continue\":\"继续\",\"ui.start.save\":\"存档\",\"ui.start.online\":\"联机\",\"ui.start.check_update\":\"检查更新\",\"ui.start.settings\":\"设置\",\"ui.start.exit\":\"退出\",\"ui.save.title\":\"存档\",\"ui.save.clear\":\"清空\",\"ui.save.load\":\"加载\",\"ui.common.cancel\":\"取消\",\"ui.pause.title\":\"暂停\",\"ui.pause.resume\":\"继续\",\"ui.pause.main_menu\":\"主界面\",\"ui.loading.title\":\"加载中\",\"ui.common.back\":\"返回\",\"ui.player_condition.power_icon\":\"能\"}") as T;
                    case "en-US.json":
                        return new TextAsset(
                            "{\"ui.start.title\":\"Start\",\"ui.start.new_game\":\"New Game\",\"ui.start.continue\":\"Continue\",\"ui.start.save\":\"Saves\",\"ui.start.online\":\"Online\",\"ui.start.check_update\":\"Check Update\",\"ui.start.settings\":\"Settings\",\"ui.start.exit\":\"Exit\",\"ui.save.title\":\"Saves\",\"ui.save.clear\":\"Clear\",\"ui.save.load\":\"Load\",\"ui.common.cancel\":\"Cancel\",\"ui.pause.title\":\"Paused\",\"ui.pause.resume\":\"Resume\",\"ui.pause.main_menu\":\"Main Menu\",\"ui.loading.title\":\"Loading\",\"ui.common.back\":\"Back\",\"ui.player_condition.power_icon\":\"P\"}") as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class FixedTextExpectation
        {
            public FixedTextExpectation(string key, string defaultText)
            {
                Key = key;
                DefaultText = defaultText;
            }

            public string Key { get; }
            public string DefaultText { get; }
        }
    }
}

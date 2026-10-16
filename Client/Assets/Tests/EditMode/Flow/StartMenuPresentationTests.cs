using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class StartMenuPresentationTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/UI/Home/Start.prefab";

        [Test]
        public void SelectionMarkFollowsHoverAndKeyboardFocusAndResetsAfterHide()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var events = new GameObject("MenuSelectionTestEvents", typeof(EventSystem));
            try
            {
                var ui = root.GetComponent<StartUI>();
                ui.BtnNewGame.gameObject.SetActive(false);
                ui.BtnContinue.gameObject.SetActive(true);
                AssertSelected(ui, ui.BtnContinue);
                var pointer = new PointerEventData(events.GetComponent<EventSystem>());
                ExecuteEvents.Execute(ui.BtnOnline.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                AssertSelected(ui, ui.BtnOnline);
                ExecuteEvents.Execute(ui.BtnSetting.gameObject, new BaseEventData(events.GetComponent<EventSystem>()), ExecuteEvents.selectHandler);
                AssertSelected(ui, ui.BtnOnline);
                ExecuteEvents.Execute(ui.BtnOnline.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                AssertSelected(ui, null);
                ExecuteEvents.Execute(ui.BtnSetting.gameObject, new BaseEventData(events.GetComponent<EventSystem>()), ExecuteEvents.selectHandler);
                AssertSelected(ui, ui.BtnSetting);
                ui.BtnSetting.gameObject.SetActive(false);
                AssertSelected(ui, ui.BtnContinue);
                ui.BtnSetting.gameObject.SetActive(true);
                ExecuteEvents.Execute(ui.BtnExit.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                AssertSelected(ui, ui.BtnExit);
                ExecuteEvents.Execute(ui.BtnExit.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                AssertSelected(ui, null);
                ExecuteEvents.Execute(ui.BtnExit.gameObject, new BaseEventData(events.GetComponent<EventSystem>()), ExecuteEvents.selectHandler);
                AssertSelected(ui, ui.BtnExit);
                root.SetActive(false);
                root.SetActive(true);
                AssertSelected(ui, ui.BtnContinue);
                ExecuteEvents.Execute(ui.BtnSetting.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                AssertSelected(ui, ui.BtnSetting);
                ExecuteEvents.Execute(ui.BtnSetting.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                ui.BtnSetting.interactable = false;
                ExecuteEvents.Execute(ui.BtnSetting.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                AssertSelected(ui, null);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(events); }
        }

        [Test]
        public void PointerCrossingMenuGapsDoesNotRestoreFirstItemOrOldKeyboardFocus()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var events = new GameObject("MenuGapTestEvents", typeof(EventSystem));
            try
            {
                var ui = root.GetComponent<StartUI>();
                ui.BtnNewGame.gameObject.SetActive(false);
                ui.BtnContinue.gameObject.SetActive(true);
                var system = events.GetComponent<EventSystem>();
                ExecuteEvents.Execute(ui.BtnContinue.gameObject, new BaseEventData(system), ExecuteEvents.selectHandler);
                var pointer = new PointerEventData(system);
                foreach (var button in new[] { ui.BtnOnline, ui.BtnSetting, ui.BtnContinue })
                {
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                    AssertSelected(ui, button);
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                    AssertSelected(ui, null);
                }
                ExecuteEvents.Execute(ui.BtnSetting.gameObject, new BaseEventData(system), ExecuteEvents.selectHandler);
                AssertSelected(ui, ui.BtnSetting);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(events); }
        }

        private static void AssertSelected(StartUI ui, Button selected)
        {
            foreach (var button in new[] { ui.BtnNewGame, ui.BtnContinue, ui.BtnOnline, ui.BtnSetting, ui.BtnExit })
            {
                var marker = button.transform.Find("PrimaryMark");
                Assert.That(marker, Is.Not.Null, button.name + " missing selection mark");
                Assert.That(marker.gameObject.activeSelf, Is.EqualTo(button == selected), button.name);
                Assert.That(button.GetComponentInChildren<TMP_Text>(true).color,
                    Is.EqualTo(button == selected
                        ? new Color(0.95f, 0.69f, 0.29f, 1f)
                        : new Color(0.94f, 0.91f, 0.84f, 1f)), button.name);
            }
        }

        [Test]
        public void MenuKeepsPrimaryActionsRightAndAuxiliaryActionsAtBottom()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var ui = prefab.GetComponent<StartUI>();
            Assert.That(ui.ButtonGroup.GetComponentsInChildren<Button>(true).Select(b => b.name),
                Is.EqualTo(new[] { "BtnNewGame", "BtnContinue", "BtnOnline", "BtnSetting", "BtnExit" }));
            Assert.That(ui.BtnCheckUpdate.transform.IsChildOf(ui.ButtonGroup), Is.False);
            Assert.That(ui.BtnExit.transform.IsChildOf(ui.ButtonGroup), Is.True);
            Assert.That(ui.VersionText.rectTransform.anchorMin.x, Is.EqualTo(0f));
            Assert.That(ui.BtnExit.GetComponentInChildren<TMP_Text>(true).fontSize,
                Is.EqualTo(ui.BtnSetting.GetComponentInChildren<TMP_Text>(true).fontSize));
            Assert.That(prefab.GetComponent<ReferenceCollector>().data.All(item => item.gameObject != null), Is.True);
            foreach (var button in ui.ButtonGroup.GetComponentsInChildren<Button>(true))
            {
                var feedback = button.GetComponent<StartMenuButtonFeedback>();
                Assert.That(feedback, Is.Not.Null, button.name);
                var serialized = new SerializedObject(feedback);
                foreach (var field in new[] { "button", "label", "selectionMark" })
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, button.name + "." + field);
            }
        }

        [Test]
        public void CommonThemeDoesNotOverwriteStartPrefabGraphics()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var label = root.GetComponent<StartUI>().BtnContinue.GetComponentInChildren<TMP_Text>();
                label.color = new Color(0.9f, 0.7f, 0.3f, 1f);
                UITheme.Apply(root);
                Assert.That(label.color, Is.EqualTo(new Color(0.9f, 0.7f, 0.3f, 1f)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EveryMenuButtonRequestsOneUiClickSoundWithoutReplacingBusinessAction()
        {
            ResetArchitecture();
            var loader = new RecordingLoader();
            ResourceLoader.SetLoaderForTests(loader);
            var audioRoot = CreateAudioBindings();
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var ui = root.GetComponent<StartUI>();
                var ctrl = new StartUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
                typeof(StartUICtrl).GetMethod("OnInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
                var buttons = new[] { ui.BtnNewGame, ui.BtnContinue, ui.BtnOnline, ui.BtnCheckUpdate, ui.BtnSetting, ui.BtnExit };
                var methods = new[] { "OnNewGameClick", "OnContinueClick", "OnOnlineClick", "OnCheckUpdateClick", "OnSettingClick", "OnExitClick" };
                int businessCalls = 0;
                for (int i = 0; i < buttons.Length; i++)
                {
                    var callback = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), ctrl,
                        typeof(StartUICtrl).GetMethod(methods[i], BindingFlags.Instance | BindingFlags.NonPublic));
                    buttons[i].onClick.RemoveListener(callback);
                    buttons[i].onClick.AddListener(() => businessCalls++);
                    int before = loader.Requests.Count;
                    buttons[i].onClick.Invoke();
                    Assert.That(loader.Requests.Count, Is.EqualTo(before + 1), buttons[i].name);
                    Assert.That(loader.Requests.Last(), Is.EqualTo("Audio/Sound/FoundationClick"));
                }
                Assert.That(businessCalls, Is.EqualTo(6));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Game.Interface.GetSystem<AudioSystem>().StopMapOwnedSounds();
                ResetArchitecture();
                ResourceLoader.ResetForTests();
                Object.DestroyImmediate(audioRoot);
            }
        }

        private static GameObject CreateAudioBindings()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
            var root = new GameObject("StartMenuAudioTest");
            var bindings = root.AddComponent<AudioRootBindings>();
            bindings.Mixer = mixer;
            bindings.MusicGroup = mixer.FindMatchingGroups("Music").First();
            bindings.SoundGroup = mixer.FindMatchingGroups("Sound").First();
            bindings.MusicSourceA = Child(root, "MusicA").AddComponent<AudioSource>();
            bindings.MusicSourceB = Child(root, "MusicB").AddComponent<AudioSource>();
            bindings.SoundPoolRoot = Child(root, "Pool").transform;
            bindings.PlayingSoundRoot = Child(root, "Playing").transform;
            return root;
        }

        private static GameObject Child(GameObject root, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class RecordingLoader : IResourceLoader
        {
            public readonly List<string> Requests = new List<string>();
            public T LoadAsset<T>(string path) where T : Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                Requests.Add(path);
                onLoaded?.Invoke(null);
                return new ResourceLoadHandle(path);
            }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}

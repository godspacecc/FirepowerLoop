using System.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class UiPrefabCompatibilityTests
    {
        [Test]
        public void MapSelectionUsesFormalBlockingDialogAndBoxPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab");
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Expedition/Box/MapSelectionOptionBox.prefab");
            var ui = prefab.GetComponent<MapSelectionUI>();
            var box = boxPrefab.GetComponent<MapSelectionOptionBoxUI>();

            Assert.That(prefab, Is.Not.Null);
            Assert.That(boxPrefab, Is.Not.Null);
            Assert.That(ui, Is.Not.Null);
            Assert.That(box, Is.Not.Null);
            Assert.That(ui.BtnClose, Is.Not.Null);
            Assert.That(ui.MapList, Is.Not.Null);
            Assert.That(ui.MapScroll, Is.Not.Null);
            Assert.That(ui.MapScroll.content, Is.SameAs(ui.MapList.transform));
            Assert.That(ui.MapList.itemPrefab, Is.SameAs(boxPrefab));
            Assert.That(box.TextName, Is.Not.Null);
            Assert.That(box.TextDetail, Is.Not.Null);
            Assert.That(box.TextState, Is.Not.Null);
            Assert.That(box.BtnSelect, Is.Not.Null);
            Assert.That(ui.BtnAction, Is.Not.Null);
            Assert.That(ui.ActionText, Is.Not.Null);
            Assert.That(ui.ActionText.GetComponent<LocalizedText>(), Is.Null, "动态状态文字由控制器刷新");
            Assert.That(prefab.GetComponentsInChildren<LocalizedText>(true)
                .Any(item => item.Key == "ui.map_selection.title"), Is.True);
            Assert.That(ui.BtnClose.GetComponentInChildren<TextMeshProUGUI>(true).text, Is.EqualTo("×"));
            Assert.That(CountMissingScriptsInHierarchy(prefab), Is.Zero);
            Assert.That(CountMissingScriptsInHierarchy(boxPrefab), Is.Zero);



        }

        [Test]
        public void Message_ButtonRoot_UsesCustomListWithHorizontalLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Notify/Message.prefab");
            var ui = prefab.GetComponent<MessageUI>();
            var list = ui.ButtonRoot.GetComponent<CustomListView>();
            var layout = ui.ButtonRoot.GetComponent<HorizontalLayoutGroup>();

            Assert.That(list, Is.Not.Null);
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.spacing, Is.GreaterThanOrEqualTo(12f));
            Assert.That(list.itemPrefab, Is.EqualTo(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/UI/Notify/Box/MessageButtonBox.prefab")));
            Assert.That(ui.BackgroundButton, Is.Not.Null);
            Assert.That(ui.InputField, Is.Not.Null);
            Assert.That(ui.InputErrorText, Is.Not.Null);
            Assert.That(ui.InputErrorText.color.r, Is.EqualTo(1f).Within(0.01f));
            var itemPrefab = list.itemPrefab;
            var itemRect = (RectTransform)itemPrefab.transform;
            var item = itemPrefab.GetComponent<MessageButtonBoxUI>();
            var layoutElement = itemPrefab.GetComponent<LayoutElement>();

            Assert.That(itemRect.sizeDelta.x, Is.EqualTo(168f));
            Assert.That(itemRect.sizeDelta.y, Is.EqualTo(44f));
            Assert.That(layoutElement, Is.Not.Null);
            Assert.That(layoutElement.preferredWidth, Is.EqualTo(168f));
            Assert.That(layoutElement.preferredHeight, Is.EqualTo(44f));
            Assert.That(item.ButtonText.enableWordWrapping, Is.False);
            Assert.That(item.ButtonText.enableAutoSizing, Is.True);
            Assert.That(item.ButtonText.fontSizeMin, Is.EqualTo(14f));
            Assert.That(item.ButtonText.fontSizeMax, Is.EqualTo(22f));
            Assert.That(item.ButtonText.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis));
            Assert.That(prefab.transform.Find("TypeList"), Is.Null);
        }

        [Test]
        public void RuntimeLog_LogList_UsesVirtualizedVerticalLoopList()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab");
            var ui = prefab.GetComponent<RuntimeLogUI>();
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/Box/RuntimeLogItemBox.prefab");
            var list = prefab.GetComponentInChildren<LoopVerticalScrollRect>(true);
            var titleText = FindText(prefab.transform, "TitleText");

            Assert.That((object)ui.LogList, Is.SameAs(list));
            AssertVerticalLoopList(list, itemPrefab, 4, 6f);
            Assert.That(((RectTransform)prefab.transform).sizeDelta, Is.EqualTo(new Vector2(900f, 640f)));
            Assert.That(ui.BtnAll, Is.Not.Null);
            Assert.That(ui.BtnNormal, Is.Not.Null);
            Assert.That(ui.BtnWarning, Is.Not.Null);
            Assert.That(ui.BtnError, Is.Not.Null);
            Assert.That(ui.DetailScrollRect, Is.Not.Null);
            Assert.That(ui.DetailText, Is.Not.Null);
            Assert.That(ui.LogList.viewport.rect.size, Is.EqualTo(new Vector2(812f, 318f)));
            Assert.That(ui.DetailScrollRect.viewport.rect.size, Is.EqualTo(new Vector2(812f, 142f)));
            Assert.That(ui.DetailScrollRect.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            foreach (var button in new[] { ui.BtnAll, ui.BtnNormal, ui.BtnWarning, ui.BtnError })
            {
                Assert.That(((RectTransform)button.transform).sizeDelta, Is.EqualTo(new Vector2(96f, 34f)));
                Assert.That(button.GetComponent<Outline>(), Is.Not.Null);
            }
            var layoutElement = itemPrefab.GetComponent<LayoutElement>();
            Assert.That(layoutElement, Is.Not.Null);
            Assert.That(layoutElement.preferredWidth, Is.EqualTo(812f));
            Assert.That(layoutElement.preferredHeight, Is.EqualTo(36f));
            var keys = prefab.GetComponentsInChildren<LocalizedText>(true);
            Assert.That(System.Array.FindAll(keys, item => item.Key.StartsWith("ui.runtime_log.", System.StringComparison.Ordinal)), Is.Empty);
            Assert.That(titleText.text, Is.EqualTo("运行日志"));
            Assert.That(ui.BtnAll.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("全部"));
            Assert.That(ui.BtnNormal.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("普通"));
            Assert.That(ui.BtnWarning.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("警告"));
            Assert.That(ui.BtnError.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("错误"));
        }

        [Test]
        public void OnlineRoom_ChatList_UsesFormalMessagePrefabThroughVerticalLoop()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab");
            var messagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Online/Box/OnlineRoomChatMessage.prefab");
            var ui = prefab.GetComponent<OnlineRoomUI>();

            Assert.That(ui.ChatMessageList, Is.Not.Null);
            Assert.That(ui.ChatMessageList.viewport, Is.Not.Null);
            Assert.That(ui.ChatMessageList.content, Is.Not.Null);
            Assert.That(ui.ChatMessageList.viewport.GetComponent<Mask>(), Is.Not.Null);
            Assert.That(ui.ChatMessageList.content.GetComponent<VerticalLayoutGroup>().spacing, Is.EqualTo(6f));
            Assert.That(ui.ChatMessageList.prefabSource.prefab, Is.EqualTo(messagePrefab));
            Assert.That(ui.ChatMessageList.prefabSource.poolSize, Is.EqualTo(4));
            Assert.That(ui.ChatMessageList.horizontal, Is.False);
            Assert.That(ui.ChatMessageList.vertical, Is.True);
            Assert.That(ui.ChatMessageList.GetComponent<ScrollRect>(), Is.Null);
            Assert.That(ui.ChatMessageList.content.GetComponent<CustomListView>(), Is.Null);
            Assert.That(ui.MemberList.GetComponent<CustomListView>(), Is.Not.Null);
        }

        [Test]
        public void TestCmdPrefabs_HaveValidScriptsAndRuntimeOnlyClickAdapter()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab");
            var ui = prefab.GetComponent<TestCmdUI>();
            var categoryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdCategoryBox.prefab");
            var entryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdEntryBox.prefab");
            var category = categoryPrefab.GetComponent<TestCmdCategoryBoxUI>();
            var entry = entryPrefab.GetComponent<TestCmdEntryBoxUI>();

            Assert.That(CountMissingScriptsInHierarchy(categoryPrefab), Is.Zero);
            Assert.That(CountMissingScriptsInHierarchy(entryPrefab), Is.Zero);
            Assert.That(ui.ResultScrollRect, Is.Not.Null);
            Assert.That(ui.ResultScrollRect.viewport, Is.SameAs(ui.ResultViewportRect));
            Assert.That(ui.ResultScrollRect.content, Is.SameAs(ui.ResultContentRect));
            Assert.That(ui.ResultViewportRect, Is.SameAs(ui.ResultScrollRect.transform));
            Assert.That(ui.ResultViewportRect.GetComponent<Image>(), Is.Not.Null);
            Assert.That(ui.ResultViewportRect.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(ui.TextResult.transform.parent, Is.SameAs(ui.ResultContentRect));
            Assert.That(category.BtnSelect.GetComponent<ButtonClickEventCtrl>(), Is.Null);
            Assert.That(entry.BtnSelect.GetComponent<ButtonClickEventCtrl>(), Is.Null);
            Assert.That(category.BtnSelect.onClick.GetPersistentEventCount(), Is.Zero);
            Assert.That(entry.BtnSelect.onClick.GetPersistentEventCount(), Is.Zero);
        }

        [Test]
        public void Setting_SettingList_UsesVerticalSectionsWithoutTypeList()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Home/Setting.prefab");
            var ui = prefab.GetComponent<SettingUI>();
            var general = ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>();
            var controls = ui.SettingControlsRoot.GetComponent<SettingControlsPageUI>();
            var layout = general.SettingList.GetComponent<VerticalLayoutGroup>();
            var hotKeyLayout = controls.HotKeyList.GetComponent<VerticalLayoutGroup>();
            var hotKeyFitter = controls.HotKeyList.GetComponent<ContentSizeFitter>();

            Assert.That(layout, Is.Not.Null);
            Assert.That(general.SettingList.GetComponent<CustomListView>(), Is.Not.Null);
            Assert.That(layout.spacing, Is.GreaterThanOrEqualTo(18f));
            Assert.That(hotKeyLayout, Is.Not.Null);
            Assert.That(hotKeyLayout.spacing, Is.GreaterThanOrEqualTo(10f));
            Assert.That(hotKeyFitter, Is.Not.Null);
            Assert.That(hotKeyFitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(prefab.transform.Find("TypeList"), Is.Null);
        }

        [Test]
        public void Setting_FixedTexts_UseLocalizationKeys()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Home/Setting.prefab");
            var components = prefab.GetComponentsInChildren<LocalizedText>(true);
            var expectedKeys = new[]
            {
                "ui.settings.category.general",
                "ui.settings.category.audio",
                "ui.settings.category.controls",
                "ui.settings.category.network",
                "ui.settings.reset",
                "ui.settings.master_volume",
                "ui.settings.music_volume",
                "ui.settings.sound_volume",
                "ui.settings.mute",
                "ui.settings.test_sound",
                "ui.settings.network.game_server",
                "ui.settings.network.resource_server",
                "ui.settings.network.apply"
            };

            foreach (var key in expectedKeys)
            {
                var matches = System.Array.FindAll(components, item => item.Key == key);
                Assert.That(matches, Is.Not.Empty, key);
                Assert.That(matches[0].GetComponent<TMP_Text>(), Is.Not.Null, key);
                Assert.That(matches[0].Texts, Is.Empty, key);
            }

            var audioTitle = System.Array.Find(
                prefab.GetComponentsInChildren<TMP_Text>(true),
                item => item.gameObject.name == "AudioTitle");

            Assert.That(audioTitle, Is.Not.Null);
            Assert.That(((RectTransform)audioTitle.transform).sizeDelta.y, Is.EqualTo(34f));
            Assert.That(audioTitle.font, Is.Not.Null);
            Assert.That(audioTitle.fontSize, Is.EqualTo(22f));
            Assert.That(audioTitle.enableAutoSizing, Is.False);
            Assert.That(audioTitle.fontSizeMin, Is.EqualTo(18f));
            Assert.That(audioTitle.fontSizeMax, Is.EqualTo(72f));
            Assert.That(audioTitle.fontStyle, Is.EqualTo(FontStyles.Normal));
            Assert.That(audioTitle.alignment, Is.EqualTo(TextAlignmentOptions.MidlineLeft));
            Assert.That(audioTitle.enableWordWrapping, Is.True);
            Assert.That(audioTitle.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
        }

        [Test]
        public void Setting_LayoutKeepsReadableDialogAndRows()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Home/Setting.prefab");
            var panel = (RectTransform)prefab.transform.GetChild(0);
            var ui = prefab.GetComponent<SettingUI>();
            var general = ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>();
            var controls = ui.SettingControlsRoot.GetComponent<SettingControlsPageUI>();
            var panelImage = panel.GetComponent<Image>();
            var settingItem = general.SettingList.itemPrefab.GetComponent<SettingItemBoxUI>();
            var hotKeyItem = controls.HotKeyList.itemPrefab.GetComponent<HotKeyItemBoxUI>();
            var settingRow = (RectTransform)settingItem.transform;
            var hotKeyRow = (RectTransform)hotKeyItem.transform;
            var actionName = (RectTransform)hotKeyItem.ActionNameText.transform;
            var bindingText = (RectTransform)hotKeyItem.BindingText.transform;

            Assert.That(panel.sizeDelta.x, Is.GreaterThanOrEqualTo(940f));
            Assert.That(panel.sizeDelta.y, Is.GreaterThanOrEqualTo(700f));
            Assert.That(panelImage.color.a, Is.GreaterThanOrEqualTo(0.99f));
            Assert.That(settingRow.sizeDelta.x, Is.GreaterThanOrEqualTo(720f));
            Assert.That(hotKeyRow.sizeDelta.x, Is.GreaterThanOrEqualTo(560f));
            Assert.That(hotKeyRow.sizeDelta.y, Is.GreaterThanOrEqualTo(42f));
            Assert.That(actionName.sizeDelta.y, Is.GreaterThanOrEqualTo(30f));
            Assert.That(bindingText.sizeDelta.y, Is.GreaterThanOrEqualTo(30f));
            Assert.That(settingItem.TextSetting, Is.Not.Null);
            Assert.That(settingItem.TextContext, Is.Not.Null);
            Assert.That(settingItem.BtnContext, Is.Not.Null);
            Assert.That(hotKeyItem.RebindButton, Is.Not.Null);
        }

        private static void AssertVerticalLoopList(
            LoopVerticalScrollRect list,
            GameObject expectedPrefab,
            int expectedPoolSize,
            float expectedSpacing)
        {
            Assert.That(list, Is.Not.Null);
            Assert.That(list.viewport, Is.Not.Null);
            Assert.That(list.content, Is.Not.Null);
            Assert.That(list.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            var layout = list.content.GetComponent<VerticalLayoutGroup>();
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.spacing, Is.GreaterThanOrEqualTo(expectedSpacing));
            Assert.That(list.prefabSource.prefab, Is.EqualTo(expectedPrefab));
            Assert.That(list.prefabSource.poolSize, Is.EqualTo(expectedPoolSize));
            Assert.That(list.horizontal, Is.False);
            Assert.That(list.vertical, Is.True);
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.That(child, Is.Not.Null, path);
            var text = child.GetComponent<TMP_Text>();
            Assert.That(text, Is.Not.Null, path);
            return text;
        }

        private static int CountMissingScriptsInHierarchy(GameObject root)
        {
            var count = 0;
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
            }

            return count;
        }
    }
}

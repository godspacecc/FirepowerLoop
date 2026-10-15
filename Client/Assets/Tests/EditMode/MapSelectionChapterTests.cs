using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>校验真实弹窗及列表项契约，防止回退为全屏、多入口或悬空生成引用。</summary>
    public sealed class MapSelectionChapterTests
    {
        [Test]
        public void DialogHasOneFooterActionAndCenteredPanel()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab");
            var collector = root.GetComponent<ReferenceCollector>();
            var action = collector.data.SingleOrDefault(entry => entry.key == "BtnAction");
            Assert.That(action, Is.Not.Null, "统一下载/出发按钮必须由正式 Prefab 提供");
            Assert.That(action.gameObject, Is.InstanceOf<Button>());
            var panel = root.transform.Find("Panel") as RectTransform;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(panel.anchorMax, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(panel.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(2), "静态弹窗只有关闭与一个主操作按钮");
            Assert.That(root.GetComponent<MapSelectionUI>().MapList.itemPrefab, Is.SameAs(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/Box/MapSelectionOptionBox.prefab")));
        }

        [Test]
        public void ChapterRowOnlySelectsAndHasNoEnterButton()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/Box/MapSelectionOptionBox.prefab");
            var view = root.GetComponent<MapSelectionOptionBoxUI>();
            Assert.That(view.BtnSelect.gameObject, Is.SameAs(root), "整张卡片只负责选中章节");
            Assert.That(root.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(1));
            Assert.That(root.GetComponent<ReferenceCollector>().data.Any(entry => entry.key == "BtnSelectText"), Is.False,
                "列表卡片不再提供独立出发按钮");
        }
    }
}

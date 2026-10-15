using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class BuildingSelectionLayoutTests
    {
        [Test]
        public void RightPanelHasVerticalScrollAndExternalRowWithWholeRowButton()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab");
            var ui = prefab.GetComponent<TemporaryBuildingSelectUI>();
            var panel = (RectTransform)prefab.transform;
            Assert.That(panel.anchorMin.x, Is.EqualTo(1));
            Assert.That(panel.anchorMax.x, Is.EqualTo(1));
            Assert.That(ui.BuildingScroll.vertical, Is.True);
            Assert.That(ui.BuildingScroll.horizontal, Is.False);
            Assert.That(ui.BuildingScroll.verticalScrollbar, Is.Not.Null);
            Assert.That(ui.BuildingScroll.verticalScrollbar.direction, Is.EqualTo(Scrollbar.Direction.BottomToTop));
            Assert.That(ui.BuildingScroll.content.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            var row = ui.BuildingList.itemPrefab.GetComponent<TemporaryBuildingOptionBoxUI>();
            Assert.That(AssetDatabase.GetAssetPath(row), Does.Contain("/Box/"));
            Assert.That(row.BtnSelect.transform, Is.SameAs(row.transform));
            Assert.That(ui.BtnDestroy.transform.IsChildOf(ui.BuildingScroll.content), Is.False);
        }

        [Test]
        public void AllConfiguredBuildingsHaveCollectedSpriteIcons()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/tbtempbuilding.bytes");
            var table = new global::TbTempBuilding(new ByteBuf(asset.bytes));
            Assert.That(table.DataList.Count, Is.EqualTo(14));
            foreach (var building in table.DataList)
            {
                Assert.That(building.IconName, Is.Not.Empty, building.Id);
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/UI/TemporaryBuilding/" + building.IconName + ".png"), Is.Not.Null, building.Id);
            }
        }
    }
}

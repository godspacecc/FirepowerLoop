using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class AndroidHudLayoutTests
    {
        [Test]
        public void ConditionPrefab_StacksStatusHealthAndWeaponRowsWithoutTouchTargets()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab");
            var ui = prefab.GetComponent<PlayerConditionHudUI>();
            var weaponField = typeof(PlayerConditionHudUI).GetField("WeaponInfoRoot");
            Assert.That(weaponField, Is.Not.Null, "武器信息行必须由正式 Prefab 生成并接线");
            var weapon = ((GameObject)weaponField.GetValue(ui)).GetComponent<RectTransform>();
            var health = ui.MobileHealthRoot.GetComponent<RectTransform>();
            var status = ui.MobileStatusRow.GetComponent<RectTransform>();
            Assert.That(ui.MobileHealthFill.rectTransform.rect.width, Is.EqualTo(360f));
            Assert.That(ui.MobileHealthRoot.transform.Find("Background").GetComponent<RectTransform>().rect.size,
                Is.EqualTo(ui.MobileHealthFill.rectTransform.rect.size), "血条底板与填充必须匹配");
            Assert.That(Bounds(status).yMin, Is.GreaterThan(Bounds(health).yMax));
            Assert.That(Bounds(status).xMin, Is.EqualTo(Bounds(ui.MobileHealthFill.transform).xMin).Within(1f));
            Assert.That(Bounds(weapon).center.y, Is.EqualTo(Bounds(status).center.y).Within(.01f));
            Assert.That(Bounds(weapon).xMin, Is.GreaterThanOrEqualTo(Bounds(status).xMax + 8f));
            Assert.That(weapon.rect.size, Is.EqualTo(new Vector2(44, 44)), "武器与体力使用同尺寸状态图标");
            Assert.That(weapon.GetComponent<Image>(), Is.Null, "移除旧长条底板");
            Assert.That(weapon.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true), Is.Empty);
            var fillField = typeof(PlayerConditionHudUI).GetField("AmmoFill");
            Assert.That(fillField, Is.Not.Null);
            var fill = (Image)fillField.GetValue(ui);
            Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(fill.sprite, Is.SameAs(ui.MobileTiliFill.sprite));
            Assert.That(ui.WeaponIcon.preserveAspect, Is.True);
            Assert.That(weapon.GetComponentInChildren<Button>(true), Is.Null, "武器信息行不承接攻击或装填输入");
        }

        [Test]
        public void ConditionPrefab_ResolvesAllGeneratedReferencesAndMobileFillSprites()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab");
            var ui = prefab.GetComponent<PlayerConditionHudUI>();
            Assert.That(prefab.GetComponents<ReferenceCollector>(), Has.Length.EqualTo(1));
            foreach (var field in typeof(PlayerConditionHudUI).GetFields().Where(f => typeof(Object).IsAssignableFrom(f.FieldType)))
                Assert.That((Object)field.GetValue(ui), Is.Not.Null, field.Name);
            foreach (var image in ui.MobileContentRoot.GetComponentsInChildren<Image>(true))
            {
                Assert.That(image.sprite != null, Is.True, image.name + " 必须有有效 Sprite，填充比例才能生效");
                Assert.That(image.raycastTarget, Is.False);
            }
            Assert.That(ui.MobileTemperatureRoot.transform.Find("TemperatureFill"), Is.Null, "温度不再显示进度环");
            Assert.That(prefab.GetComponent<ReferenceCollector>().data.Single(d => d.key == "MobileTemperatureStem").gameObject, Is.InstanceOf<Image>());
            Assert.That(ui.MobileTemperatureStem.fillMethod, Is.EqualTo(Image.FillMethod.Vertical));
            Assert.That(ui.MobileTemperatureStem.rectTransform.rect.height, Is.GreaterThanOrEqualTo(32f));
            Assert.That(ui.MobileHealthFill.sprite.border, Is.EqualTo(Vector4.zero), "血条使用等粗素材");
            foreach (var name in new[] { "Power", "Water", "Tili" })
            {
                var root = ui.MobileContentRoot.GetComponentsInChildren<Transform>(true).Single(t => t.name == name + "Root");
                Assert.That(root.Find(name + "IconText"), Is.Null);
                var icon = root.Find(name + "Icon").GetComponent<Image>();
                Assert.That(AssetDatabase.GetAssetPath(icon.sprite), Does.StartWith("Assets/Game/Art/"));
                Assert.That(root.Find(name + "ValueText"), Is.Null, "手机不显示详细数值");
                Assert.That(root.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(44, 44)));
            }
            Assert.That(ui.MobileTemperatureRoot.transform.Find("TemperatureValueText"), Is.Null);
            var row = ui.MobilePowerRoot.transform.parent.GetComponent<HorizontalLayoutGroup>();
            Assert.That(row, Is.Not.Null);
            Assert.That(row.spacing, Is.EqualTo(8f));
            Assert.That(row.childAlignment, Is.EqualTo(TextAnchor.MiddleLeft));
            Assert.That(row.GetComponent<RectTransform>().rect.width, Is.EqualTo(200f));
            Assert.That(row.childForceExpandWidth, Is.False);
            var mobileFills = new[] { ui.MobilePowerFill, ui.MobileWaterFill, ui.MobileTiliFill };
            var originalFills = new[] { ui.PowerFill, ui.WaterFill, ui.TiliFill };
            for (var i = 0; i < mobileFills.Length; i++)
            {
                Assert.That(mobileFills[i].sprite, Is.SameAs(originalFills[i].sprite), "手机保留原圆环素材");
                Assert.That(mobileFills[i].fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
                Assert.That(mobileFills[i].color, Is.EqualTo(originalFills[i].color));
            }
            var mission = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab").GetComponent<ExpeditionHudUI>();
            foreach (var field in typeof(ExpeditionHudUI).GetFields().Where(f => typeof(Object).IsAssignableFrom(f.FieldType)))
                Assert.That((Object)field.GetValue(mission), Is.Not.Null, field.Name);
        }

        [TestCase(1920f, 1080f)]
        [TestCase(2400f, 1080f)]
        public void LandscapeLayout_SeparatesStateStripFromBothHands(float width, float height)
        {
            var parent = new GameObject("HudTestArea", typeof(RectTransform));
            parent.SetActive(false);
            var area = (RectTransform)parent.transform;
            area.sizeDelta = new Vector2(width, height);
            try
            {
                var condition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab"), area).GetComponent<PlayerConditionHudUI>();
                var controls = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"), area).GetComponent<MobileJoystickUI>();
                var status = Bounds(condition.MobileContentRoot.transform);
                var bag = Bounds(controls.InventoryButton.transform);
                var move = Bounds(controls.MoveJoystickRoot.transform);
                Assert.That(status.Overlaps(move), Is.False);
                Assert.That(status.Overlaps(Bounds(controls.AttackButton.transform)), Is.False);
                Assert.That(status.Overlaps(bag), Is.False);
                Assert.That(bag.Overlaps(move), Is.False);
                Assert.That(bag.yMin, Is.GreaterThan(move.yMax));
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)condition.MobilePowerRoot.transform.parent);
                Assert.That(Bounds(condition.MobilePowerRoot.transform).yMin,
                    Is.GreaterThan(Bounds(condition.MobileHealthRoot.transform).yMax));
                var layout = new MobileControlLayoutData();
                // 兼容旧设备保存的默认武器中心，并在两种宽屏比例下保持可见左右边缘一致。
                layout.Weapon.X = .5f;
                MobileControlLayoutGeometry.RenderHud(area, (RectTransform)condition.MobileHealthRoot.transform,
                    layout.HudForDisplay(MobileLayoutHud.Health, area.rect.size));
                MobileControlLayoutGeometry.RenderHud(area, (RectTransform)condition.WeaponInfoRoot.transform,
                    layout.HudForDisplay(MobileLayoutHud.Weapon, area.rect.size));
                var weapon = Bounds(condition.WeaponInfoRoot.transform);
                var fill = Bounds(condition.MobileHealthFill.transform);
                Assert.That(weapon.yMin, Is.GreaterThan(fill.yMax));
                Assert.That(weapon.width, Is.EqualTo(44f).Within(.01f));
                Assert.That(layout.Weapon.X, Is.EqualTo(.5f), "显示适配不能改写设备设置");
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void HudDefaultMigration_LowersOnlyUntouchedPositions()
        {
            var data = new MobileControlLayoutData();
            data.Health.Y = 107f / 1080f;
            data.Status.Y = 170f / 1080f;
            data.Weapon.X = 984f / 1920f;
            data.Weapon.Y = 54f / 1080f;
            var size = new Vector2(1920, 1080);
            Assert.That(data.HudForDisplay(MobileLayoutHud.Health, size).Y * 1080, Is.EqualTo(67).Within(.01f));
            Assert.That(data.HudForDisplay(MobileLayoutHud.Status, size).Y * 1080, Is.EqualTo(130).Within(.01f));
            Assert.That(data.HudForDisplay(MobileLayoutHud.Weapon, size).Y * 1080, Is.EqualTo(130).Within(.01f));
            Assert.That(data.Health.Y, Is.EqualTo(107f / 1080f), "显示适配不覆写保存位置");
            data.Status.X = .6f;
            Assert.That(data.HudForDisplay(MobileLayoutHud.Status, size).Y, Is.EqualTo(170f / 1080f));
            var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab")
                .GetComponent<MobileJoystickUI>();
            Assert.That(ui.SprintButton, Is.Not.Null);
            Assert.That(ui.SprintIcon.sprite, Is.Not.Null);
            Assert.That(ui.SprintIcon.raycastTarget, Is.False);
            Assert.That(ui.SprintButton.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(100, 100)));
        }

        private static Rect Bounds(Transform transform)
        {
            var corners = new Vector3[4];
            ((RectTransform)transform).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}

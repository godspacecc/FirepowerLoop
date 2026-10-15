using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class MobileBaseCombatTests
    {
        [Test]
        public void BasePrefab_HasIndependentAimFireAndReloadControls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab");
            Assert.That(prefab.transform.Find("ContentRoot/BaseCombatRoot"), Is.Not.Null,
                "基地三指操作应有独立控件容器，与越肩攻击隔离");
            var ui = prefab.GetComponent<MobileJoystickUI>();
            Assert.That(ui.BaseAimSurface, Is.Not.Null);
            Assert.That(ui.BaseAimSurface.Handle, Is.Not.Null);
            Assert.That(ui.BaseFirePress, Is.Not.Null);
            Assert.That(ui.BaseReloadPress, Is.Not.Null);
            Assert.That(ui.BaseFirePress.gameObject.activeSelf, Is.True, "基地容器显示时独立开火键必须可见");
            Assert.That(ui.BaseReloadPress.gameObject.activeSelf, Is.True, "基地容器显示时装填键必须可见");
            Assert.That(ui.BaseReloadProgress.raycastTarget, Is.False);
            var fire = (RectTransform)ui.BaseFirePress.transform;
            var reload = (RectTransform)ui.BaseReloadPress.transform;
            Assert.That(fire.anchorMin.y, Is.GreaterThan(reload.anchorMin.y));
            Assert.That(fire.sizeDelta.x, Is.GreaterThan(reload.sizeDelta.x));
            Assert.That(ui.BaseAimSurface.GetComponent<UnityEngine.InputSystem.OnScreen.OnScreenStick>(), Is.Null,
                "基地方向不能向街机虚拟右摇杆设备提交射击");
            foreach (var name in new[] { "BaseCombatRoot", "BaseAimRoot", "BaseAimOpacity", "BaseAimSurface", "BaseFirePress", "BaseReloadPress", "BaseReloadProgress" })
            {
                var field = typeof(MobileJoystickUI).GetField(name);
                var value = field.GetValue(ui) as Object;
                Assert.That(value, Is.Not.Null, name);
                Assert.That(prefab.GetComponent<ReferenceCollector>().data.Exists(x => x.key == name && x.gameObject == value), Is.True, name);
            }
        }

        [Test]
        public void FirePress_OwnsOneFingerAndResetRequiresFreshPress()
        {
            var root = new GameObject("press", typeof(RectTransform), typeof(MobileCombatPressView));
            try
            {
                var press = root.GetComponent<MobileCombatPressView>();
                var downs = 0; var ups = 0;
                press.Pressed += () => downs++;
                press.Released += () => ups++;
                var a = new PointerEventData(null) { pointerId = 11 };
                var b = new PointerEventData(null) { pointerId = 12 };
                press.OnPointerDown(a);
                press.OnPointerDown(b);
                press.OnPointerUp(b);
                Assert.That(downs, Is.EqualTo(1)); Assert.That(ups, Is.Zero);
                press.ResetCapture();
                press.OnPointerUp(a);
                Assert.That(ups, Is.EqualTo(1));
                press.OnPointerDown(b);
                Assert.That(downs, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AimStick_DoesNotAcceptOtherFingerDragOrRelease()
        {
            var root = new GameObject("aim", typeof(RectTransform), typeof(MobileAimStickView));
            var handle = new GameObject("handle", typeof(RectTransform));
            handle.transform.SetParent(root.transform, false);
            try
            {
                var stick = root.GetComponent<MobileAimStickView>();
                stick.Handle = (RectTransform)handle.transform;
                var last = Vector2.zero;
                stick.DirectionChanged += value => last = value;
                var a = new PointerEventData(null) { pointerId = 21, position = new Vector2(75, 0) };
                var b = new PointerEventData(null) { pointerId = 22, position = new Vector2(0, 75) };
                stick.OnPointerDown(a);
                Assert.That(last, Is.EqualTo(Vector2.right));
                stick.OnPointerDown(b); stick.OnDrag(b); stick.OnPointerUp(b);
                Assert.That(last, Is.EqualTo(Vector2.right));
                stick.OnPointerUp(a);
                Assert.That(last, Is.EqualTo(Vector2.zero));
                stick.OnDrag(a);
                Assert.That(last, Is.EqualTo(Vector2.zero));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

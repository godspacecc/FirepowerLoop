using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class PlayerTiliRingTests
    {
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab";
        private const string RingSpritePath = "Assets/Game/Art/UI/TiliRing.png";

        [Test]
        public void PlayerPrefabHasCameraFacingTiliRingAndSingleProductView()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.NotNull(prefab);
            var views = prefab.GetComponentsInChildren<PlayerView>(true);
            Assert.That(views.Length, Is.EqualTo(1));
            Assert.That(views[0].Validate(out var error), Is.True, error);

            var canvas = prefab.transform.Find("TiliRingCanvas");
            Assert.NotNull(canvas);
            var root = canvas.Find("TiliRingRoot");
            Assert.NotNull(root);
            var background = root.Find("Background")?.GetComponent<Image>();
            var fill = root.Find("Fill")?.GetComponent<Image>();
            Assert.NotNull(background);
            Assert.NotNull(fill);

            var importer = AssetImporter.GetAtPath(RingSpritePath) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RingSpritePath);
            Assert.NotNull(importer);
            Assert.NotNull(texture);
            Assert.That(texture.width, Is.EqualTo(128));
            Assert.That(texture.height, Is.EqualTo(128));
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.maxTextureSize, Is.EqualTo(128));

            var canvasComponent = canvas.GetComponent<Canvas>();
            var canvasRect = canvas.GetComponent<RectTransform>();
            var rootRect = root.GetComponent<RectTransform>();
            Assert.NotNull(canvasComponent);
            Assert.NotNull(canvasRect);
            Assert.NotNull(rootRect);
            Assert.That(canvasComponent.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(canvas.gameObject.activeSelf, Is.False);
            AssertVector3(canvas.localPosition, new Vector3(0f, 1f, 0f));
            AssertVector3(canvas.localScale, new Vector3(0.01f, 0.01f, 0.01f));
            AssertVector2(canvasRect.sizeDelta, new Vector2(160f, 160f));
            AssertVector2(rootRect.sizeDelta, new Vector2(52f, 52f));
            AssertVector2(rootRect.anchoredPosition, new Vector2(-48f, -42f));
            Assert.That(root.GetComponentsInChildren<Image>(true).Length, Is.EqualTo(2));
            Assert.That(background.raycastTarget, Is.False);
            Assert.That(fill.raycastTarget, Is.False);
            Assert.That(background.sprite, Is.Not.Null);
            Assert.That(fill.sprite, Is.SameAs(background.sprite));
            Assert.That(background.type, Is.EqualTo(Image.Type.Simple));
            AssertColor(background.color, new Color(0f, 0f, 0f, 0.75f));
            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(fill.fillClockwise, Is.True);
            Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.Origin360.Top));
            Assert.That(prefab.transform.Find("TiliArcCanvas"), Is.Null);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true).Any(item => item.name.StartsWith("TiliArcSegment")), Is.False);
        }

        [Test]
        public void TiliRingAlwaysStaysHiddenForEveryConditionInput()
        {
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath));
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var canvas = instance.transform.Find("TiliRingCanvas").gameObject;
                var fill = canvas.transform.Find("TiliRingRoot/Fill").GetComponent<Image>();

                view.SetTiliRing(100f, 100f, false, false);
                Assert.That(canvas.activeSelf, Is.False);

                view.SetTiliRing(50f, 100f, false, false);
                Assert.That(canvas.activeSelf, Is.False);
                Assert.That(fill.enabled, Is.False);

                view.SetTiliRing(0f, 100f, false, true);
                Assert.That(canvas.activeSelf, Is.False);
                Assert.That(fill.enabled, Is.False);

                view.TickCombatFeedback(0.05f);
                Assert.That(canvas.activeSelf, Is.False);
                Assert.That(fill.enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TiliRingHandlesInvalidMaximumWithoutThrowing()
        {
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath));
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var canvas = instance.transform.Find("TiliRingCanvas").gameObject;
                var fill = canvas.transform.Find("TiliRingRoot/Fill").GetComponent<Image>();

                Assert.DoesNotThrow(() => view.SetTiliRing(5f, 0f, false, false));
                Assert.That(canvas.activeSelf, Is.False);
                Assert.That(fill.enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FaceWorldHudFacesTiliRingAndPreservesSerializedOffsets()
        {
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath));
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var canvas = instance.transform.Find("TiliRingCanvas");
                var ringRoot = (RectTransform)canvas.Find("TiliRingRoot");
                var ringAnchoredPosition = ringRoot.anchoredPosition;
                var canvasLocalPosition = canvas.localPosition;
                var cameraPosition = new Vector3(0f, 5f, -6f);
                var cameraUp = Vector3.up;

                view.FaceWorldHud(cameraPosition, cameraUp);

                AssertFaces(canvas, cameraPosition, cameraUp);
                Assert.That(ringRoot.anchoredPosition, Is.EqualTo(ringAnchoredPosition));
                Assert.That(canvas.localPosition, Is.EqualTo(canvasLocalPosition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            return target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);
        }

        private static void AssertFaces(Transform target, Vector3 cameraPosition, Vector3 cameraUp)
        {
            var expected = Quaternion.LookRotation(target.position - cameraPosition, cameraUp);
            Assert.That(Quaternion.Angle(target.rotation, expected), Is.LessThan(0.001f));
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));
        }
    }
}

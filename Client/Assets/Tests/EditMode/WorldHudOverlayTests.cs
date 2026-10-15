using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class WorldHudOverlayTests
    {
        private const string ShaderPath = "Assets/Game/Shaders/WorldHudOverlay.shader";
        private const string MaterialPath = "Assets/Game/Materials/WorldHudOverlay.mat";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab";
        private const string EnemyPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab";
        private const string ArcherPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxArcher.prefab";
        private const string ShaderName = "UI/AnotherDay World HUD Overlay";

        [Test]
        public void OverlayShaderAndMaterialKeepFixedRenderContract()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.NotNull(shader);
            Assert.That(shader.name, Is.EqualTo(ShaderName));
            Assert.NotNull(material);
            Assert.That(material.shader, Is.SameAs(shader));
            AssertColor(material.color, Color.white);

            var shaderSource = File.ReadAllText(ShaderPath);
            Assert.That(shaderSource, Does.Contain("\"Queue\"=\"Overlay\""));
            Assert.That(shaderSource, Does.Contain("\"CanUseSpriteAtlas\"=\"True\""));
            Assert.That(shaderSource, Does.Contain("ZTest Always"));
            Assert.That(shaderSource, Does.Contain("ZWrite Off"));
            Assert.That(shaderSource, Does.Contain("Blend SrcAlpha OneMinusSrcAlpha"));
            Assert.That(shaderSource, Does.Contain("ColorMask [_ColorMask]"));
            Assert.That(shaderSource, Does.Contain("_StencilComp"));
            Assert.That(shaderSource, Does.Contain("_StencilReadMask"));
            Assert.That(shaderSource, Does.Contain("_StencilWriteMask"));
            Assert.That(shaderSource, Does.Contain("_ClipRect"));
            Assert.That(shaderSource, Does.Contain("_TextureSampleAdd"));
            Assert.That(shaderSource, Does.Not.Contain("ZTest [unity_GUIZTestMode]"));
        }

        [Test]
        public void PlayerAndEnemyWorldHudImagesUseOneOverlayMaterial()
        {
            var material = LoadOverlayMaterial();
            var player = LoadPrefab(PlayerPrefabPath);
            var enemy = LoadPrefab(EnemyPrefabPath);

            Assert.That(PlayerTiliBackground(player).material, Is.SameAs(material));
            Assert.That(PlayerTiliFill(player).material, Is.SameAs(material));
            Assert.That(EnemyHealthFill(enemy).material, Is.SameAs(material));

            AssertOverlayImagePaths(
                player,
                material,
                "TiliRingCanvas/TiliRingRoot/Background",
                "TiliRingCanvas/TiliRingRoot/Fill");
            AssertOverlayImagePaths(
                enemy,
                material,
                "EnemyHealthBarRoot/Canvas/Fill");
        }

        [Test]
        public void OverlayMaterialDoesNotChangeWorldHudLayoutOrBehaviorComponents()
        {
            var player = LoadPrefab(PlayerPrefabPath);
            var enemy = LoadPrefab(EnemyPrefabPath);
            var tiliCanvas = RequiredTransform(player, "TiliRingCanvas");
            var tiliRoot = RequiredTransform(player, "TiliRingCanvas/TiliRingRoot");
            var enemyHealthRoot = RequiredTransform(enemy, "EnemyHealthBarRoot");
            var enemyHealthCanvas = RequiredTransform(enemy, "EnemyHealthBarRoot/Canvas");

            AssertWorldCanvas(tiliCanvas, new Vector2(160f, 160f), new Vector3(0.01f, 0.01f, 0.01f));
            AssertVector3(tiliCanvas.localPosition, new Vector3(0f, 1f, 0f));
            AssertVector2(((RectTransform)tiliRoot).sizeDelta, new Vector2(52f, 52f));
            AssertVector2(((RectTransform)tiliRoot).anchoredPosition, new Vector2(-48f, -42f));

            AssertVector3(enemyHealthRoot.localPosition, new Vector3(0f, 1.75f, 0f));
            AssertWorldCanvas(enemyHealthCanvas, new Vector2(100f, 11f), new Vector3(0.01f, 0.01f, 0.01f));

            var tiliBackground = PlayerTiliBackground(player);
            AssertColor(tiliBackground.color, new Color(0f, 0f, 0f, 0.75f));
            Assert.That(tiliBackground.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(tiliBackground.raycastTarget, Is.False);

            var tiliFill = PlayerTiliFill(player);
            AssertColor(tiliFill.color, new Color(0.25f, 0.95f, 0.45f, 0.95f));
            Assert.That(tiliFill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(tiliFill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(tiliFill.fillClockwise, Is.True);
            Assert.That(tiliFill.fillOrigin, Is.EqualTo((int)Image.Origin360.Top));
            Assert.That(tiliFill.raycastTarget, Is.False);

            var enemyHealthFill = EnemyHealthFill(enemy);
            AssertColor(enemyHealthFill.color, Color.red);
            Assert.That(enemyHealthFill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(enemyHealthFill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
            Assert.That(enemyHealthFill.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
            Assert.That(enemyHealthFill.raycastTarget, Is.False);
        }

        [Test]
        public void UnitHealthBarRootsDefaultInactiveInProductPrefabs()
        {
            var player = LoadPrefab(PlayerPrefabPath);
            var enemy = LoadPrefab(EnemyPrefabPath);
            var archer = LoadPrefab(ArcherPrefabPath);

            Assert.That(player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(RequiredTransform(enemy, "EnemyHealthBarRoot").gameObject.activeSelf, Is.False);
            Assert.That(RequiredTransform(archer, "EnemyHealthBarRoot").gameObject.activeSelf, Is.False);
        }

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab, path);
            return prefab;
        }

        private static Material LoadOverlayMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.NotNull(material);
            return material;
        }

        private static Image PlayerTiliBackground(GameObject player)
        {
            return RequiredImage(player, "TiliRingCanvas/TiliRingRoot/Background");
        }

        private static Image PlayerTiliFill(GameObject player)
        {
            return RequiredImage(player, "TiliRingCanvas/TiliRingRoot/Fill");
        }

        private static Image EnemyHealthFill(GameObject enemy)
        {
            return RequiredImage(enemy, "EnemyHealthBarRoot/Canvas/Fill");
        }

        private static Transform RequiredTransform(GameObject root, string path)
        {
            var target = root.transform.Find(path);
            Assert.NotNull(target, path);
            return target;
        }

        private static Image RequiredImage(GameObject root, string path)
        {
            var target = RequiredTransform(root, path).GetComponent<Image>();
            Assert.NotNull(target, path);
            return target;
        }

        private static void AssertOverlayImagePaths(GameObject root, Material material, params string[] expectedPaths)
        {
            var actualPaths = root.GetComponentsInChildren<Image>(true)
                .Where(image => image.material == material)
                .Select(image => TransformPath(root.transform, image.transform))
                .OrderBy(path => path)
                .ToArray();
            Assert.That(actualPaths, Is.EqualTo(expectedPaths.OrderBy(path => path).ToArray()));
        }

        private static string TransformPath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            var path = target.name;
            var current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static void AssertWorldCanvas(Transform target, Vector2 size, Vector3 scale)
        {
            var canvas = target.GetComponent<Canvas>();
            Assert.NotNull(canvas, target.name);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            AssertVector2(((RectTransform)target).sizeDelta, size);
            AssertVector3(target.localScale, scale);
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

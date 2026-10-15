using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class MapFeatureReferenceOverlayTests
    {
        private const string LocalScenePath =
            "Assets/Game/Scenes/GeneratedMaps/HeJiaVillageLocalPreview/HeJiaVillageLocalPreview.unity";
        private Type overlayType;
        private Type layerType;
        private Type windowType;
        private Scene localScene;
        private bool openedForTest;
        private GameObject originalSelection;
        private object overlay;
        private EditorWindow window;

        [SetUp]
        public void SetUp()
        {
            overlayType = RequireType("Game.MapFeatureReferenceOverlay, Assembly-CSharp-Editor");
            layerType = overlayType.GetNestedType("FeatureLayer", BindingFlags.Public);
            windowType = RequireType("Game.MapFeatureReferenceOverlayWindow, Assembly-CSharp-Editor");
            originalSelection = Selection.activeGameObject;
            localScene = SceneManager.GetSceneByPath(LocalScenePath);
            openedForTest = !localScene.isLoaded;
            if (openedForTest) localScene = EditorSceneManager.OpenScene(LocalScenePath, OpenSceneMode.Additive);
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null)
            {
                Invoke(window, "ShutdownForTests");
                Object.DestroyImmediate(window);
            }
            if (overlay != null) Invoke(overlay, "Release");
            Selection.activeGameObject = originalSelection;
            if (openedForTest && localScene.IsValid() && localScene.isLoaded)
                EditorSceneManager.CloseScene(localScene, true);
        }

        [Test]
        public void MenuAndAllLayers_AreAvailable()
        {
            Assert.That((string)windowType.GetField("MenuPath").GetRawConstantValue(),
                Is.EqualTo("Tools/地图/局部散图制作/地物参考"));
            Assert.That(windowType.GetMethod("OpenWindow", BindingFlags.Public | BindingFlags.Static), Is.Not.Null);
            var expected = new[]
            {
                ("Forest", "Forest"), ("BuiltUp", "BuiltUp"),
                ("Water", "Water"), ("Roads", "Road")
            };
            var getPath = overlayType.GetMethod("GetTexturePath", BindingFlags.Public | BindingFlags.Static);
            foreach (var (layerName, filePart) in expected)
            {
                var path = (string)getPath.Invoke(null, new[] { Enum.Parse(layerType, layerName) });
                Assert.That(path, Does.EndWith("HeJiaVillage_" + filePart + "Reference.png"));
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(texture, Is.Not.Null, path);
                Assert.That((texture.width, texture.height), Is.EqualTo((2048, 2048)));
            }
        }

        [Test]
        public void LocalTerrainMesh_CropsFullReferenceAndFollowsHeight()
        {
            var terrain = LocalTerrain();
            var wasDirty = localScene.isDirty;
            overlay = Activator.CreateInstance(overlayType);
            var args = new object[] { terrain, Enum.Parse(layerType, "Forest"), 0.45f, null };
            Assert.That(overlayType.GetMethod("TryBuild").Invoke(overlay, args), Is.True, args[3] as string);
            var mesh = (Mesh)overlayType.GetProperty("PreviewMesh").GetValue(overlay);
            Assert.That(mesh.vertexCount, Is.EqualTo(129 * 129));
            Assert.That(mesh.vertices[0].y, Is.EqualTo(terrain.terrainData.GetInterpolatedHeight(0f, 0f) + 0.3f).Within(0.0001f));
            Assert.That(mesh.vertices[mesh.vertexCount - 1].x, Is.EqualTo(768f));
            Assert.That(mesh.vertices[mesh.vertexCount - 1].z, Is.EqualTo(768f));
            Assert.That(mesh.uv[0], Is.EqualTo(new Vector2(720f / 2048f, 976f / 2048f)));
            Assert.That(mesh.uv[mesh.vertexCount - 1],
                Is.EqualTo(new Vector2(1488f / 2048f, 1744f / 2048f)));
            Assert.That(localScene.isDirty, Is.EqualTo(wasDirty));
        }

        [Test]
        public void Resolution_UsesOnlyFormalLocalTerrain()
        {
            var terrain = LocalTerrain();
            var resolve = overlayType.GetMethod("TryResolveTerrain");
            var selected = new object[] { terrain.gameObject, null, null };
            Assert.That(resolve.Invoke(null, selected), Is.True, selected[2] as string);
            Assert.That(selected[1], Is.SameAs(terrain));
            var fallback = new object[] { null, null, null };
            Assert.That(resolve.Invoke(null, fallback), Is.True, fallback[2] as string);
            Assert.That(fallback[1], Is.SameAs(terrain));

            var fakeData = new TerrainData { heightmapResolution = 33, size = new Vector3(768f, 330f, 768f) };
            var fakeRoot = Terrain.CreateTerrainGameObject(fakeData);
            try
            {
                var invalid = new object[] { fakeRoot, null, null };
                Assert.That(resolve.Invoke(null, invalid), Is.False);
                Assert.That((string)invalid[2], Does.Contain("正式和家村局部场景"));
            }
            finally
            {
                Object.DestroyImmediate(fakeRoot);
                Object.DestroyImmediate(fakeData);
            }
        }

        [Test]
        public void WindowRefreshAndClose_KeepLocalSceneClean()
        {
            var terrain = LocalTerrain();
            var wasDirty = localScene.isDirty;
            var roots = localScene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray();
            Selection.activeGameObject = terrain.gameObject;
            window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            Invoke(window, "InitializeForTests");
            Invoke(window, "RefreshForTests");
            Assert.That((bool)windowType.GetProperty("HasDrawableResources").GetValue(window), Is.True);
            Invoke(window, "ShutdownForTests");
            Assert.That((bool)windowType.GetProperty("HasDrawableResources").GetValue(window), Is.False);
            Assert.That(localScene.isDirty, Is.EqualTo(wasDirty));
            Assert.That(localScene.GetRootGameObjects().Select(root => root.GetInstanceID()), Is.EqualTo(roots));
        }

        private Terrain LocalTerrain() => localScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Terrain>(true)).Single();

        private static Type RequireType(string name) =>
            Type.GetType(name) ?? throw new InvalidOperationException("找不到类型：" + name);

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);
    }
}

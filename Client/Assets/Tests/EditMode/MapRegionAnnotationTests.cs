using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class MapRegionAnnotationTests
    {
        private Type assetType;
        private Type layerKindType;
        private Type strokeModeType;
        private Type rasterizerType;
        private Type windowType;
        private ScriptableObject annotation;

        [SetUp]
        public void SetUp()
        {
            assetType = RequireType("Game.MapRegionAnnotationAsset, Assembly-CSharp-Editor");
            layerKindType = RequireType("Game.MapRegionLayerKind, Assembly-CSharp-Editor");
            strokeModeType = RequireType("Game.MapRegionStrokeMode, Assembly-CSharp-Editor");
            rasterizerType = RequireType("Game.MapRegionAnnotationRasterizer, Assembly-CSharp-Editor");
            windowType = RequireType("Game.MapRegionAnnotationWindow, Assembly-CSharp-Editor");
            annotation = ScriptableObject.CreateInstance(assetType);
            Invoke(annotation, "Initialize", "Docs/base.png", "abc123", 64, 32);
        }

        [TearDown]
        public void TearDown()
        {
            if (annotation != null) UnityEngine.Object.DestroyImmediate(annotation);
        }

        [Test]
        public void Defaults_HaveStableEmptyLayersAndImageIdentity()
        {
            Assert.That(Get<string>(annotation, "BaseImageProjectPath"), Is.EqualTo("Docs/base.png"));
            Assert.That(Get<string>(annotation, "BaseImageSha256"), Is.EqualTo("abc123"));
            Assert.That(Get<int>(annotation, "ImageWidth"), Is.EqualTo(64));
            Assert.That(Get<int>(annotation, "ImageHeight"), Is.EqualTo(32));
            var layers = (IList)Get<object>(annotation, "Layers");
            Assert.That(layers, Has.Count.EqualTo(5));
            foreach (var layer in layers) Assert.That(((IList)Get<object>(layer, "Strokes")), Is.Empty);
            var validation = new object[] { null };
            Assert.That(assetType.GetMethod("Validate").Invoke(annotation, validation), Is.True, validation[0] as string);
        }

        [Test]
        public void PaintThenErase_ReplaysDeterministicallyWithoutTouchingScene()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneDirtyBefore = scene.isDirty;
            var rootCountBefore = scene.rootCount;
            var water = Enum.Parse(layerKindType, "Water");
            AddStroke(water, "Paint", 5f, new[] { new Vector2(10f, 10f), new Vector2(30f, 10f) });
            AddStroke(water, "Erase", 2f, new[] { new Vector2(20f, 10f) });

            var coverage = (byte[])rasterizerType.GetMethod("BuildCoverage")
                .Invoke(null, new[] { annotation, water });
            Assert.That(coverage[10 * 64 + 10], Is.EqualTo(255));
            Assert.That(coverage[10 * 64 + 20], Is.Zero);
            Assert.That(coverage[10 * 64 + 30], Is.EqualTo(255));
            Assert.That(scene.rootCount, Is.EqualTo(rootCountBefore));
            Assert.That(scene.isDirty, Is.EqualTo(sceneDirtyBefore));
        }

        [Test]
        public void ActiveStrokeCoverage_MatchesCommittedBrushWidth()
        {
            var water = Enum.Parse(layerKindType, "Water");
            var paint = Enum.Parse(strokeModeType, "Paint");
            var points = new[] { new Vector2(10f, 10f), new Vector2(30f, 10f) };
            var preview = (byte[])rasterizerType.GetMethod("BuildCoverageWithStroke")
                .Invoke(null, new object[] { annotation, water, paint, 5f, points });

            AddStroke(water, "Paint", 5f, points);
            var committed = (byte[])rasterizerType.GetMethod("BuildCoverage")
                .Invoke(null, new[] { annotation, water });
            Assert.That(preview, Is.EqualTo(committed));
        }

        [Test]
        public void ClearLayer_PreservesOtherSemanticLayers()
        {
            var water = Enum.Parse(layerKindType, "Water");
            var road = Enum.Parse(layerKindType, "Road");
            AddStroke(water, "Paint", 3f, new[] { new Vector2(8f, 8f) });
            AddStroke(road, "Paint", 3f, new[] { new Vector2(40f, 20f) });
            assetType.GetMethod("ClearLayer").Invoke(annotation, new[] { water });

            Assert.That(GetLayerStrokeCount(water), Is.Zero);
            Assert.That(GetLayerStrokeCount(road), Is.EqualTo(1));
        }

        [Test]
        public void OpenAsset_WithMismatchedImageSize_KeepsCurrentAnnotation()
        {
            var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            var candidate = ScriptableObject.CreateInstance(assetType);
            try
            {
                var current = Get<object>(window, "Annotation");
                var currentTexture = Get<object>(window, "BaseTexture");
                Assert.That(current, Is.Not.Null);
                Assert.That(currentTexture, Is.Not.Null);
                Invoke(candidate, "Initialize", GetConstant<string>(windowType, "DefaultBaseImageProjectPath"),
                    GetConstant<string>(windowType, "DefaultBaseImageSha256"), 1, 1);

                Assert.That(windowType.GetMethod("LoadAssetForTests").Invoke(window, new object[] { candidate }),
                    Is.False);
                Assert.That(Get<object>(window, "Annotation"), Is.SameAs(current));
                Assert.That(Get<object>(window, "BaseTexture"), Is.SameAs(currentTexture));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void WindowContract_RequiresSelectedVersionedBaseAndIndependentAsset()
        {
            Assert.That(GetConstant<string>(windowType, "MenuPath"), Is.EqualTo("Tools/地图/局部散图制作/区域标注（和家村卫星图）"));
            Assert.That(GetConstant<string>(windowType, "DefaultBaseImageProjectPath"),
                Does.EndWith("HeJiaVillage_GaodeSatellite_112.192228_27.634943_z17_558x699_20260927.png"));
            Assert.That(GetConstant<string>(windowType, "DefaultBaseImageSha256"),
                Is.EqualTo("8368c080bdd0a7c48555d0aad018793a90971a3fdb7c5b60317c2047f48207b8"));
            Assert.That(GetConstant<string>(windowType, "PreferencesKeyPrefix"),
                Is.EqualTo("AnotherDay.MapRegionAnnotation."));
            Assert.That(GetConstant<string>(windowType, "DefaultAssetDirectory"),
                Is.EqualTo("Assets/Game/MapTerrainSources/HeJiaVillage/RegionAnnotations"));
            Assert.That(GetConstant<string>(windowType, "DefaultAssetPath"),
                Is.EqualTo("Assets/Game/MapTerrainSources/HeJiaVillage/RegionAnnotations/HeJiaVillage_RegionAnnotations.asset"));
            Assert.That(windowType.GetMethod("OpenWindow", BindingFlags.Public | BindingFlags.Static), Is.Not.Null);
            Assert.That(windowType.GetMethod("CreateBlankForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(windowType.GetMethod("LoadAssetForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(windowType.GetMethod("SaveForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(windowType.GetMethod("SaveAsForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(windowType.GetMethod("ExportAllMasksForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(windowType.GetMethod("SavePreferencesForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            Assert.That(windowType.GetMethod("LoadPreferencesForTests", BindingFlags.Public | BindingFlags.Instance), Is.Not.Null);
            var pan = windowType.GetMethod("IsPanInputForTests", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pan.Invoke(null, new object[] { 1, false }), Is.True, "右键应直接平移");
            Assert.That(pan.Invoke(null, new object[] { 2, false }), Is.True, "中键应直接平移");
            Assert.That(pan.Invoke(null, new object[] { 0, true }), Is.True, "Alt+左键应平移");
            Assert.That(pan.Invoke(null, new object[] { 0, false }), Is.False, "普通左键必须保留给画笔");
        }

        private void AddStroke(object layer, string modeName, float radius, Vector2[] points)
        {
            assetType.GetMethod("AddStroke").Invoke(annotation,
                new object[] { layer, Enum.Parse(strokeModeType, modeName), radius, points });
        }

        private int GetLayerStrokeCount(object kind)
        {
            var layer = assetType.GetMethod("GetLayer").Invoke(annotation, new[] { kind });
            return ((IList)Get<object>(layer, "Strokes")).Count;
        }

        private static T Get<T>(object target, string property)
        {
            return (T)target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(target);
        }

        private static T GetConstant<T>(Type type, string name)
        {
            return (T)type.GetField(name, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
        }

        private static object Invoke(object target, string method, params object[] arguments)
        {
            return target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)
                .Invoke(target, arguments);
        }

        private static Type RequireType(string name)
        {
            return Type.GetType(name) ?? throw new InvalidOperationException("找不到类型：" + name);
        }
    }
}

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class MapHeightAnnotationTests
    {
        private Type assetType;
        private Type modeType;
        private Type rasterizerType;
        private Type windowType;
        private ScriptableObject annotation;

        [SetUp]
        public void SetUp()
        {
            assetType = RequireType("Game.MapHeightAnnotationAsset, Assembly-CSharp-Editor");
            modeType = RequireType("Game.MapHeightStrokeMode, Assembly-CSharp-Editor");
            rasterizerType = RequireType("Game.MapHeightAnnotationRasterizer, Assembly-CSharp-Editor");
            windowType = RequireType("Game.MapHeightAnnotationWindow, Assembly-CSharp-Editor");
            annotation = ScriptableObject.CreateInstance(assetType);
            Invoke(annotation, "Initialize", "Client/Assets/base.png", "image-hash", 558, 699,
                "Assets/dem.png", "dem-hash");
        }

        [TearDown]
        public void TearDown()
        {
            if (annotation != null) UnityEngine.Object.DestroyImmediate(annotation);
        }

        [Test]
        public void ConfirmedFrame_MapsCornersInsideFullDemWithoutStretchingFullMap()
        {
            Assert.That(Get<double>(annotation, "GcjWest"), Is.EqualTo(112.189234655d));
            Assert.That(Get<double>(annotation, "GcjEast"), Is.EqualTo(112.195221345d));
            Assert.That(Get<double>(annotation, "GcjSouth"), Is.EqualTo(27.631620987d));
            Assert.That(Get<double>(annotation, "GcjNorth"), Is.EqualTo(27.638264912d));
            Assert.That(MapPoint(new Vector2(0f, 0f)), Is.EqualTo(new Vector2(805.817f, 1725.722f)));
            Assert.That(MapPoint(new Vector2(557f, 698f)), Is.EqualTo(new Vector2(1404.891f, 996.532f)));
            var center = MapPoint(new Vector2(278.5f, 349f));
            Assert.That(center.x, Is.EqualTo(1105.345f).Within(0.01f));
            Assert.That(center.y, Is.EqualTo(1361.127f).Within(0.01f));
            Assert.That(center.x, Is.GreaterThan(0f).And.LessThan(2048f));
            Assert.That(center.y, Is.GreaterThan(0f).And.LessThan(2048f));
        }

        [Test]
        public void RelativeStrokes_AccumulateAndResetToDemInStrokeOrder()
        {
            AddStroke("AddOffset", 3f, 5f, new[] { new Vector2(100f, 100f) });
            AddStroke("AddOffset", -1f, 5f, new[] { new Vector2(100f, 100f) });
            var delta = BuildDelta();
            Assert.That(delta[100 * 558 + 100], Is.EqualTo(2f));
            AddStroke("ResetToDem", 0f, 2f, new[] { new Vector2(100f, 100f) });
            delta = BuildDelta();
            Assert.That(delta[100 * 558 + 100], Is.Zero);
            Assert.That(delta[100 * 558 + 104], Is.EqualTo(2f));
        }

        [Test]
        public void ActiveStrokeDelta_MatchesCommittedBrushWidth()
        {
            var mode = Enum.Parse(modeType, "AddOffset");
            var points = new[] { new Vector2(100f, 100f), new Vector2(120f, 100f) };
            var preview = (float[])rasterizerType.GetMethod("BuildDeltaMetersWithStroke")
                .Invoke(null, new object[] { annotation, mode, 3f, 5f, points });

            AddStroke("AddOffset", 3f, 5f, points);
            Assert.That(preview, Is.EqualTo(BuildDelta()));
        }

        [Test]
        public void DemSample_UsesExistingEncodedBaseline()
        {
            var dem = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            dem.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
            dem.Apply();
            try
            {
                var method = rasterizerType.GetMethod("SampleDemMeters", BindingFlags.Public | BindingFlags.Static);
                var height = (float)method.Invoke(null, new object[] { annotation, dem, new Vector2(279f, 350f) });
                var quantizedGray = 128f / 255f;
                Assert.That(height, Is.EqualTo(120f + quantizedGray * 330f).Within(0.01f));
            }
            finally { UnityEngine.Object.DestroyImmediate(dem); }
        }

        [Test]
        public void ContourOverlay_DrawsLinesWithoutChangingDemOrAnnotation()
        {
            var dem = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            dem.SetPixels(new[] { Color.black, Color.black, Color.white, Color.white });
            dem.Apply();
            Texture2D overlay = null;
            try
            {
                overlay = (Texture2D)rasterizerType.GetMethod("CreateContourOverlay")
                    .Invoke(null, new object[] { annotation, dem });
                Assert.That(overlay.width, Is.EqualTo(558));
                Assert.That(overlay.height, Is.EqualTo(699));
                Assert.That(Array.Exists(overlay.GetPixels32(), pixel => pixel.a > 0), Is.True);
                Assert.That(((ICollection)Get<object>(annotation, "Strokes")).Count, Is.Zero);
            }
            finally
            {
                if (overlay != null) UnityEngine.Object.DestroyImmediate(overlay);
                UnityEngine.Object.DestroyImmediate(dem);
            }
        }

        [Test]
        public void SmallHeightEdit_HasOpaqueBoundaryInPreview()
        {
            var dem = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            dem.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
            dem.Apply();
            Texture2D preview = null;
            Texture2D satellitePreview = null;
            try
            {
                AddStroke("AddOffset", 2f, 5f, new[] { new Vector2(100f, 100f) });
                preview = (Texture2D)rasterizerType.GetMethod("CreatePreviewFromDelta")
                    .Invoke(null, new object[] { annotation, dem, 0.15f, BuildDelta(), null });
                // 纹理 Y 轴与底图像素 Y 轴相反；圆形笔触外缘应保持不透明。
                var edge = preview.GetPixel(105, 698 - 100);
                var outside = preview.GetPixel(106, 698 - 100);
                Assert.That(edge.a, Is.EqualTo(1f).Within(0.01f));
                Assert.That(outside.a, Is.EqualTo(0.15f).Within(0.01f));
                satellitePreview = (Texture2D)rasterizerType.GetMethod("CreatePreviewFromDeltaWithMode")
                    .Invoke(null, new object[] { annotation, dem, 0.15f, BuildDelta(), null, false });
                Assert.That(satellitePreview.GetPixel(106, 698 - 100).a, Is.Zero);
                Assert.That(satellitePreview.GetPixel(100, 698 - 100).a, Is.GreaterThan(0f));
            }
            finally
            {
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                if (satellitePreview != null) UnityEngine.Object.DestroyImmediate(satellitePreview);
                UnityEngine.Object.DestroyImmediate(dem);
            }
        }

        [Test]
        public void Window_LoadsConfirmedImageAndDemWithoutChangingScene()
        {
            var scene = SceneManager.GetActiveScene();
            var dirty = scene.isDirty;
            var roots = scene.rootCount;
            var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
            try
            {
                Assert.That((bool)windowType.GetMethod("LoadOrCreateForTests").Invoke(window, null), Is.True);
                Assert.That(Get<bool>(window, "HasValidSources"), Is.True);
                Assert.That(scene.isDirty, Is.EqualTo(dirty));
                Assert.That(scene.rootCount, Is.EqualTo(roots));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void WindowContract_LocksConfirmedSourcesAndKeepsTerrainExportSeparate()
        {
            Assert.That(GetConstant<string>(windowType, "MenuPath"), Is.EqualTo("Tools/地图/局部散图制作/高度草图（和家村卫星图）"));
            Assert.That(GetConstant<string>(windowType, "BaseImageSha256"),
                Is.EqualTo("8368c080bdd0a7c48555d0aad018793a90971a3fdb7c5b60317c2047f48207b8"));
            Assert.That(GetConstant<string>(windowType, "DemSha256"),
                Is.EqualTo("c2502edc692ec340ebb0eeec97aeeabf06dd76b0d51c4c2db43b5b5c11d34d80"));
            Assert.That(GetConstant<string>(windowType, "ExportProjectPath"), Does.EndWith("HeightDeltaMeters.exr"));
            Assert.That(GetConstant<string>(windowType, "PreferencesKeyPrefix"),
                Is.EqualTo("AnotherDay.MapHeightAnnotation."));
            Assert.That(windowType.GetMethod("SaveForTests"), Is.Not.Null);
            Assert.That(windowType.GetMethod("ExportForTests"), Is.Not.Null);
            Assert.That(windowType.GetMethod("SavePreferencesForTests"), Is.Not.Null);
            Assert.That(windowType.GetMethod("LoadPreferencesForTests"), Is.Not.Null);
            var pan = windowType.GetMethod("IsPanInputForTests", BindingFlags.Public | BindingFlags.Static);
            Assert.That(pan.Invoke(null, new object[] { 1, false }), Is.True, "右键应直接平移");
            Assert.That(pan.Invoke(null, new object[] { 2, false }), Is.True, "中键应直接平移");
            Assert.That(pan.Invoke(null, new object[] { 0, true }), Is.True, "Alt+左键应平移");
            Assert.That(pan.Invoke(null, new object[] { 0, false }), Is.False, "普通左键必须保留给画笔");
        }

        private Vector2 MapPoint(Vector2 point)
        {
            return (Vector2)assetType.GetMethod("ImagePointToUnity").Invoke(annotation, new object[] { point });
        }

        private void AddStroke(string mode, float delta, float radius, Vector2[] points)
        {
            assetType.GetMethod("AddStroke").Invoke(annotation,
                new object[] { Enum.Parse(modeType, mode), delta, radius, points });
        }

        private float[] BuildDelta()
        {
            return (float[])rasterizerType.GetMethod("BuildDeltaMeters").Invoke(null, new object[] { annotation });
        }

        private static object Invoke(object target, string method, params object[] arguments)
        {
            return target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)
                .Invoke(target, arguments);
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

        private static Type RequireType(string name)
        {
            return Type.GetType(name) ?? throw new InvalidOperationException("找不到类型：" + name);
        }
    }
}

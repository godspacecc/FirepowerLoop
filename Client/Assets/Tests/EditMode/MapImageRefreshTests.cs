using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MapImageRefreshTests
    {
        [Test]
        public void NearbyRefreshKeepsRemoteHistory()
        {
            var black = new Color32[64];
            for (var i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
            var display = new MapDisplayDefinition("test", "", 0, 0, 80, 80);
            var cache = new MapImageMemory(display, 8, black);
            try
            {
                var white = new Color32[64];
                for (var i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
                cache.ApplyNearbyPatch(new RectInt(0, 0, 8, 8), white, new Vector3(-25, 0, -25), 12f);
                var image = cache.Texture;
                Assert.That(image.GetPixel(1, 1).r, Is.EqualTo(1));
                Assert.That(image.GetPixel(6, 6).r, Is.Zero, "远处当前画面不得覆盖历史地图");
                Assert.That(image.GetPixel(0, 0).r, Is.Zero, "圆形范围外的拍摄角落仍保留历史");
                cache.ApplyNearbyPatch(new RectInt(0, 0, 8, 8), white, new Vector3(25, 0, 25), 12f);
                Assert.That(image.GetPixel(6, 6).r, Is.EqualTo(1), "走近后才更新此前远处的画面");
                Assert.That(image.GetPixel(1, 1).r, Is.EqualTo(1), "离开后保留此前观察到的画面");
            }
            finally { ((IDisposable)cache).Dispose(); }
        }

        [Test]
        public void MapEdgesClipCaptureWithoutChangingScale()
        {
            var display = new MapDisplayDefinition("test", "", 0, 0, 80, 80);
            var region = MapNearbyCapture.GetRegion(display, new Vector3(-40, 0, -40), 12, 8);
            Assert.That(region, Is.EqualTo(new RectInt(0, 0, 2, 2)));
            Assert.That(MapNearbyCapture.GetRegion(display, new Vector3(41, 0, 0), 12, 8).width, Is.Zero);
        }

        [Test]
        public void InvalidPatchLeavesHistoryUntouchedAndReleaseIsIdempotent()
        {
            var display = new MapDisplayDefinition("test", "", 0, 0, 80, 80);
            var cache = new MapImageMemory(display, 8, new Color32[64]);
            Assert.Throws<ArgumentException>(() => cache.ApplyNearbyPatch(new RectInt(7, 7, 2, 2), new Color32[4], Vector3.zero, 12));
            Assert.That(cache.Texture.GetPixel(7, 7).r, Is.Zero);
            var texture = cache.Texture;
            cache.Dispose(); cache.Dispose();
            Assert.That(texture == null, Is.True);
        }

        [Test]
        public void SceneCaptureReflectsRemovalAndAdditionWhileHidingUnitsAndBuildingPreview()
        {
            var root = new GameObject("MapCaptureFixture");
            var dynamicRoot = new GameObject("Dynamic"); dynamicRoot.transform.SetParent(root.transform);
            var context = root.AddComponent<MapContext>();
            typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, root);
            typeof(MapContext).GetField("dynamicObjectRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, dynamicRoot.transform);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null);
            var red = new Material(shader); var green = new Material(shader);
            red.SetColor("_BaseColor", Color.red); red.SetColor("_Color", Color.red);
            green.SetColor("_BaseColor", Color.green); green.SetColor("_Color", Color.green);
            var display = new MapDisplayDefinition("test", "", 0, 0, 8, 8);
            var terrainObject = Cube(root.transform, red, 0);
            var actor = Cube(dynamicRoot.transform, green, 2);
            var preview = Cube(dynamicRoot.transform, green, 4);
            var building = preview.AddComponent<TemporaryBuildingView>();
            var region = new RectInt(0, 0, 8, 8);
            try
            {
                var first = MapNearbyCapture.Capture(context, display, region, 8, 0);
                Assert.That(first[36].r, Is.GreaterThan(.8f * 255), "树木应可见，单位及建筑预览不入图");
                Assert.That(actor.GetComponent<Renderer>().forceRenderingOff, Is.False, "拍摄后恢复角色渲染");
                building.SetCommitted(false, out _);
                var committed = MapNearbyCapture.Capture(context, display, region, 8, 0);
                Assert.That(committed[36].g, Is.GreaterThan(.8f * 255), "已提交建筑必须入图");
                preview.SetActive(false); terrainObject.SetActive(false);
                var removed = MapNearbyCapture.Capture(context, display, region, 8, 0);
                Assert.That(removed[36].r, Is.LessThan(.4f * 255), "移除树木后旧树冠应消失");
                terrainObject.SetActive(true);
                var added = MapNearbyCapture.Capture(context, display, region, 8, 0);
                Assert.That(added[36].r, Is.GreaterThan(.8f * 255), "重新种植后应出现");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(red); UnityEngine.Object.DestroyImmediate(green); }
        }

        private static GameObject Cube(Transform parent, Material material, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(parent);
            go.transform.position = new Vector3(0, height, 0); go.transform.localScale = new Vector3(4, 1, 4);
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
    }
}

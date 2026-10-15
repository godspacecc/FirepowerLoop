using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过实际 Shader 渲染检查河床透视；不以材质字段值代替画面结果。</summary>
    public sealed class ValleyRiverRenderingTests
    {
        [Test] public void RiverSurface_RevealsUnderlyingRiverbedColor()
        {
            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Environment/CivilisationExpedition/River/RiverWater.mat");
            Assert.That(water, Is.Not.Null);
            var root = new GameObject("RiverRenderingProbe");
            var target = new RenderTexture(32, 32, 24);
            var pixels = new Texture2D(32, 32, TextureFormat.RGB24, false);
            var bedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var previous = RenderTexture.active;
            try
            {
                var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
                surface.transform.SetParent(root.transform);
                surface.layer = 31;
                surface.GetComponent<Renderer>().sharedMaterial = water;
                var bed = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bed.transform.SetParent(root.transform); bed.transform.position = new Vector3(0, 0, .25f);
                bed.layer = 31; bed.GetComponent<Renderer>().sharedMaterial = bedMaterial;
                var cameraObject = new GameObject("ProbeCamera");
                cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -2);
                camera.orthographic = true; camera.orthographicSize = .4f;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.targetTexture = target;
                Color Sample(Color bed)
                {
                    bedMaterial.SetColor("_BaseColor", bed); camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); pixels.Apply();
                    return pixels.GetPixel(16, 16);
                }
                var red = Sample(Color.red); var green = Sample(Color.green);
                Assert.That(Mathf.Abs(red.r - green.r) + Mathf.Abs(red.g - green.g), Is.GreaterThan(.4f),
                    "水面后方颜色必须影响最终画面，不能被不透明水色完全遮挡");
                System.IO.Directory.CreateDirectory("Assets/Tests/Results~/RiverValley");
                System.IO.File.WriteAllBytes("Assets/Tests/Results~/RiverValley/riverbed-transparency.png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(bedMaterial);
            }
        }
    }
}

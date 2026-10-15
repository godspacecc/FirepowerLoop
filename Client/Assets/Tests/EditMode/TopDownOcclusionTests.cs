using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>只验证相机遮挡表现和释放；碰撞与原始共享材质必须保持不变。</summary>
    public sealed class TopDownOcclusionTests
    {
        private GameObject root;
        private Material original;
        private Material transparent;
        private Component view;
        private Renderer body;
        private Renderer roof;
        private BoxCollider volume;

        [SetUp]
        public void Setup()
        {
            var type = typeof(PlayerCameraCtrl).Assembly.GetType("Game.CameraOccluderView");
            Assert.That(type, Is.Not.Null, "缺少相机遮挡表现适配器");
            root = new GameObject("Occlusion test");
            original = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            transparent = new Material(original);
            transparent.SetFloat("_Surface", 1);
            transparent.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            var bodyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyObject.transform.SetParent(root.transform, false);
            body = bodyObject.GetComponent<Renderer>(); body.sharedMaterial = original;
            var roofObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roofObject.transform.SetParent(root.transform, false);
            roofObject.transform.localPosition = Vector3.up * 3;
            roof = roofObject.GetComponent<Renderer>(); roof.sharedMaterial = original;
            volume = root.AddComponent<BoxCollider>(); volume.isTrigger = true; volume.size = new Vector3(4,4,4);
            view = root.AddComponent(type);
            Field("BodyRenderers", new[] { body }); Field("RoofRenderers", new[] { roof });
            Field("InteriorCollider", volume); Field("TransparentMaterial", transparent);
        }

        [TearDown]
        public void Cleanup()
        {
            if(root != null) Object.DestroyImmediate(root);
            if(original != null) Object.DestroyImmediate(original);
            if(transparent != null) Object.DestroyImmediate(transparent);
        }

        [Test]
        public void BlockedObjectFadesAndRestoresWithoutChangingCollisionOrSharedMaterial()
        {
            var collider = body.GetComponent<Collider>();
            var beforeColor = original.GetColor("_BaseColor");
            Refresh(true, new Vector3(10,0,10), .1f);
            Assert.That(body.sharedMaterial, Is.Not.SameAs(original));
            Assert.That(Alpha(body), Is.GreaterThan(.18f).And.LessThan(1));
            Refresh(true, new Vector3(10,0,10), 1f);
            Assert.That(Alpha(body), Is.EqualTo(.18f).Within(.001f));
            Assert.That(collider.enabled, Is.True); Assert.That(volume.isTrigger, Is.True);
            Assert.That(original.GetColor("_BaseColor"), Is.EqualTo(beforeColor));
            Refresh(false, new Vector3(10,0,10), 1f);
            Assert.That(body.sharedMaterial, Is.SameAs(original));
            Assert.That(body.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InsideShelterFadesRoofKeepsOutlineAndUsesExitMargin(bool obstructed)
        {
            var roofCollider = roof.GetComponent<Collider>();
            Refresh(obstructed, Vector3.zero, .1f);
            Assert.That(roof.enabled, Is.True);
            Assert.That(Alpha(roof), Is.GreaterThan(.18f).And.LessThan(1f), "进入棚内时平滑淡化");
            Refresh(obstructed, Vector3.zero, 1f);
            Assert.That(roof.enabled, Is.True, "棚顶保留轮廓，不完全隐藏");
            Assert.That(Alpha(roof), Is.EqualTo(.18f).Within(.001f));
            Assert.That(body.enabled, Is.True);
            Assert.That(roofCollider.enabled, Is.True);
            Assert.That(volume.enabled, Is.True);
            if (!obstructed) Assert.That(body.sharedMaterial, Is.SameAs(original));
            Refresh(false, new Vector3(2.1f,0,0), 1f);
            Assert.That(roof.enabled, Is.True);
            Assert.That(Alpha(roof), Is.EqualTo(.18f).Within(.001f), "边缘缓冲避免反复闪烁");
            Refresh(false, new Vector3(2.5f,0,0), .1f);
            Assert.That(Alpha(roof), Is.GreaterThan(.18f).And.LessThan(1f), "离开棚内时平滑恢复");
            Refresh(false, new Vector3(2.5f,0,0), 1f);
            Assert.That(roof.enabled, Is.True); Assert.That(roof.sharedMaterial, Is.SameAs(original));
        }

        [Test]
        public void DisableRestoresRoofRendererAndMaterialImmediately()
        {
            Refresh(true, Vector3.zero, 1f);
            root.SetActive(false);
            Assert.That(roof.enabled, Is.True); Assert.That(body.sharedMaterial, Is.SameAs(original));
            root.SetActive(true);
            Assert.That(roof.sharedMaterial, Is.SameAs(original));
        }

        [TestCase(.1f)]
        [TestCase(1f)]
        public void AlphaClippedLeavesBlendTextureAlphaWhileFading(float deltaTime)
        {
            original.SetFloat("_AlphaClip", 1f);
            original.SetFloat("_AlphaToMask", 1f);
            original.SetFloat("_Cutoff", .42f);
            original.EnableKeyword("_ALPHATEST_ON");
            transparent.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            transparent.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transparent.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            var leafTexture = new Texture2D(2, 1);
            leafTexture.SetPixels(new[] { Color.clear, Color.white });
            leafTexture.Apply();
            original.SetTexture("_BaseMap", leafTexture);
            var second = new Material(original);
            second.SetFloat("_Cutoff", .6f);
            body.sharedMaterials = new[] { original, second };
            try
            {
                Refresh(true, new Vector3(10,0,10), deltaTime);
                var alpha = Alpha(body);
                var faded = body.sharedMaterials;
                foreach (var material in faded)
                {
                    Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.False, "透明叶片不能被裁剪或 MSAA 锐化回不透明");
                    Assert.That(material.GetFloat("_AlphaClip"), Is.Zero);
                    Assert.That(material.GetFloat("_AlphaToMask"), Is.Zero, "低透明度不能被覆盖率过滤掉");
                    Assert.That(material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"), Is.True, "混合参数与模板关键字必须一致，否则 18% Alpha 仍呈现实色");
                    Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(leafTexture), "保留纹理 Alpha，镂空不变成矩形");
                }
                Assert.That(alpha, Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(original.GetFloat("_Cutoff"), Is.EqualTo(.42f));
                Assert.That(original.GetFloat("_AlphaToMask"), Is.EqualTo(1f));
                Assert.That(original.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(second.GetFloat("_Cutoff"), Is.EqualTo(.6f));
                Refresh(false, new Vector3(10,0,10), 1f);
                Assert.That(body.sharedMaterials, Is.EqualTo(new[] { original, second }));
            }
            finally
            {
                view.GetType().GetMethod("Restore").Invoke(view, null);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(leafTexture);
            }
        }

        [Test]
        public void CameraControllerDetectsMarkedObstructionAndDisposeRestoresIt()
        {
            volume.enabled = false; roof.enabled = false;
            root.transform.position = new Vector3(0,2.6f,-2);
            root.transform.localScale = Vector3.one * 2;
            var cameraObject = new GameObject("Camera test");
            var targetObject = new GameObject("Target test");
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(0,4,-4);
            object ctrl = null;
            try
            {
                var type = typeof(PlayerCameraCtrl).Assembly.GetType("Game.TopDownOcclusionCtrl");
                Assert.That(type, Is.Not.Null);
                ctrl = Activator.CreateInstance(type, camera, targetObject.transform);
                Physics.SyncTransforms(); type.GetMethod("Tick").Invoke(ctrl, new object[] { 1f });
                Assert.That(body.sharedMaterial, Is.Not.SameAs(original));
                ((IDisposable)ctrl).Dispose();
                Assert.That(body.sharedMaterial, Is.SameAs(original));
            }
            finally
            {
                (ctrl as IDisposable)?.Dispose(); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void FadeRecoveryKeepsLatestExternalTintInsteadOfRevivingOldHighlight()
        {
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", Color.red); body.SetPropertyBlock(block);
            Refresh(true, new Vector3(10,0,10), 1f);
            body.SetPropertyBlock(null); // 模拟拆除高亮已被玩法清除。
            Refresh(true, new Vector3(10,0,10), 1f);
            Refresh(false, new Vector3(10,0,10), 1f);
            body.GetPropertyBlock(block);
            Assert.That(block.HasColor("_BaseColor") ? block.GetColor("_BaseColor").r : original.GetColor("_BaseColor").r, Is.EqualTo(1f));
            Assert.That(block.HasColor("_BaseColor") ? block.GetColor("_BaseColor").g : original.GetColor("_BaseColor").g, Is.EqualTo(1f));
        }

        [Test]
        public void FadingDoesNotOverrideLodManagedRendererEnabledState()
        {
            Refresh(true, Vector3.zero, 1f);
            body.enabled = false; // 模拟 LODGroup 在下一帧切到其他网格。
            roof.enabled = false;
            Refresh(true, Vector3.zero, 1f);
            Assert.That(body.enabled, Is.False);
            Assert.That(roof.enabled, Is.False);
            Refresh(false, new Vector3(10,0,10), 1f);
            Assert.That(body.enabled, Is.False);
            Assert.That(roof.enabled, Is.False);
        }
        [Test]
        public void WideCanopyFadesEvenWhenThinTrunkMissesTheCameraRay()
        {
            volume.enabled = false; roof.enabled = false;
            root.transform.position = new Vector3(1.2f,2.6f,-2);
            body.transform.localScale = new Vector3(3,2,3);
            ((BoxCollider)body.GetComponent<Collider>()).size = new Vector3(.03f,1,.03f);
            var cameraObject = new GameObject("Canopy camera"); var targetObject = new GameObject("Canopy target");
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(0,4,-4);
            using(var controller = new TopDownOcclusionCtrl(camera,targetObject.transform))
            {
                try {Physics.SyncTransforms(); controller.Tick(1f); Assert.That(body.sharedMaterial,Is.Not.SameAs(original));}
                finally {Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(targetObject);}
            }
        }

        [Test]
        public void UncommittedBuildingPreviewKeepsItsOriginalPresentation()
        {
            root.AddComponent<TemporaryBuildingView>(); volume.enabled = false; roof.enabled = false;
            root.transform.position = new Vector3(0,2.6f,-2); root.transform.localScale=Vector3.one*2;
            var cameraObject = new GameObject("Preview camera"); var targetObject = new GameObject("Preview target");
            var camera = cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(0,4,-4);
            using(var controller = new TopDownOcclusionCtrl(camera,targetObject.transform))
            {
                try {Physics.SyncTransforms();controller.Tick(1f);Assert.That(body.sharedMaterial,Is.SameAs(original));}
                finally {Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(targetObject);}
            }
        }
        private void Field(string name, object value) => view.GetType().GetField(name).SetValue(view, value);
        private void Refresh(bool blocked, Vector3 position, float delta) =>
            view.GetType().GetMethod("Refresh").Invoke(view, new object[] { blocked, position, delta });
        private static float Alpha(Renderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            return block.GetColor("_BaseColor").a;
        }
    }
}

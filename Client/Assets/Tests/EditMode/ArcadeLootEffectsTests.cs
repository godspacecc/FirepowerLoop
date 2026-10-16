using System.Linq;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirepowerLoop.Tests
{
    public sealed class ArcadeLootEffectsTests
    {
        private const string Folder = "Assets/Game/Prefabs/SceneObjects/";

        [Test]
        public void DropOpacityFadesOwnedTransparentModelNameAndBeamWithoutChangingAssets()
        {
            var view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ResourcePickupView>(Folder + "TestResourcePickup.prefab"));
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "ArcadeDropCoin.prefab");
                var source = prefab.GetComponentInChildren<Renderer>().sharedMaterial;
                var originalColor = source.GetColor("_BaseColor");
                var originalSurface = source.GetFloat("_Surface");
                view.SetArcadePresentation(prefab, "Prefabs/SceneObjects/ArcadeDropCoin");
                var visual = view.transform.Find("ArcadeDropVisual");
                var beam = view.transform.Find("ArcadeLootBeam");
                var beamSources = beam.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).ToArray();
                var beamColors = beamSources.Select(m => m.GetColor("_BaseColor")).ToArray();
                var labelAlpha = view.NameLabel.alpha;
                SetOpacity(view, 0.25f);
                var material = visual.GetComponentInChildren<Renderer>().sharedMaterial;
                Assert.That(material, Is.Not.SameAs(source));
                Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1f));
                Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
                Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.SrcAlpha));
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(material.renderQueue, Is.EqualTo(3000));
                Assert.That(material.GetColor("_BaseColor").a, Is.EqualTo(originalColor.a * 0.25f).Within(0.001f));
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(source.GetTexture("_BaseMap")));
                Assert.That(view.NameLabel.alpha, Is.EqualTo(labelAlpha * 0.25f).Within(0.001f));
                var fadedBeam = beam.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).ToArray();
                for (var i = 0; i < fadedBeam.Length; i++)
                {
                    Assert.That(fadedBeam[i], Is.Not.SameAs(beamSources[i]));
                    Assert.That(fadedBeam[i].GetColor("_BaseColor").a, Is.EqualTo(beamColors[i].a * 0.25f).Within(0.001f));
                    Assert.That(beamSources[i].GetColor("_BaseColor"), Is.EqualTo(beamColors[i]));
                }
                view.SetArcadePresentation(prefab, "Prefabs/SceneObjects/ArcadeDropCoin");
                SetOpacity(view, 0.5f);
                Assert.That(visual.GetComponentInChildren<Renderer>().sharedMaterial, Is.SameAs(material));
                Assert.That(material.GetColor("_BaseColor").a, Is.EqualTo(originalColor.a * 0.5f).Within(0.001f));
                Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(originalColor));
                Assert.That(source.GetFloat("_Surface"), Is.EqualTo(originalSurface));
                view.ClearArcadePresentation();
                Assert.That(material == null, Is.True, "实例材质必须随视觉释放");
                Assert.That(fadedBeam.All(m => m == null), Is.True);
                Assert.That(view.NameLabel.alpha, Is.EqualTo(labelAlpha));
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void RecycledDropRestoresOpacityAndFadingKeepsInteractionAvailable()
        {
            var view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ResourcePickupView>(Folder + "TestResourcePickup.prefab"));
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "ArcadeRifle.prefab");
                view.SetArcadePresentation(prefab, "Prefabs/SceneObjects/ArcadeRifle");
                var alpha = view.NameLabel.alpha;
                var invoked = false;
                view.InteractionRequested += () => invoked = true;
                view.SetInteractionAvailable(true);
                SetOpacity(view, 0f);
                view.Interact();
                Assert.That(invoked, Is.True);
                var oldMaterial = view.transform.Find("ArcadeDropVisual").GetComponentInChildren<Renderer>().sharedMaterial;
                var controller = new LootCtrl(null);
                try
                {
                    typeof(LootCtrl).GetMethod("RecycleArcadeDrop", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(controller, new object[] { view });
                }
                finally
                {
                    // 测试随后自行复用此物资，取回所有权后释放Controller的生命周期订阅。
                    typeof(LootCtrl).GetMethod("TakeArcadeDrop", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(controller, null);
                    controller.ReleaseAll();
                }
                Assert.That(oldMaterial == null, Is.True);
                view.gameObject.SetActive(true);
                view.SetArcadePresentation(prefab, "Prefabs/SceneObjects/ArcadeRifle");
                Assert.That(view.NameLabel.alpha, Is.EqualTo(alpha));
                var source = prefab.GetComponentInChildren<Renderer>().sharedMaterial;
                Assert.That(view.transform.Find("ArcadeDropVisual").GetComponentInChildren<Renderer>().sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(source.GetColor("_BaseColor")));
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        private static void SetOpacity(ResourcePickupView view, float opacity)
        {
            var method = typeof(ResourcePickupView).GetMethod("SetArcadeOpacity");
            Assert.That(method, Is.Not.Null, "缺少地面物资实例渐隐入口");
            method.Invoke(view, new object[] { opacity });
        }

        [Test]
        public void LootBeamIsIdempotentAndReleasedWithDropVisual()
        {
            var view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ResourcePickupView>(Folder + "TestResourcePickup.prefab"));
            try
            {
                Assert.That(view.transform.Find("ArcadeLootBeam"), Is.Null);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "ArcadeDropMagnet.prefab");
                view.SetArcadePresentation(prefab, "Prefabs/SceneObjects/ArcadeDropMagnet");
                var beam = view.transform.Find("ArcadeLootBeam");
                Assert.That(beam, Is.Not.Null);
                view.SetArcadePresentation(prefab, "Prefabs/SceneObjects/ArcadeDropMagnet");
                Assert.That(view.transform.Find("ArcadeLootBeam"), Is.SameAs(beam));
                Assert.That(view.GetComponentsInChildren<Transform>().Count(t => t.name == "ArcadeLootBeam"), Is.EqualTo(1));
                view.ClearArcadePresentation();
                Assert.That(view.transform.Find("ArcadeLootBeam"), Is.Null);
                Assert.That(view.transform.Find("ArcadeDropVisual"), Is.Null);
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void GoldWeaponAndPowerupUseDistinctBeamColors()
        {
            var view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ResourcePickupView>(Folder + "TestResourcePickup.prefab"));
            try
            {
                var colors = new System.Collections.Generic.List<Color>();
                foreach (var name in new[] { "ArcadeDropCoin", "ArcadeRifle", "ArcadeDropMagnet" })
                {
                    view.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab"), "Prefabs/SceneObjects/" + name);
                    var beam = view.transform.Find("ArcadeLootBeam");
                    Assert.That(beam, Is.Not.Null, name);
                    colors.Add(beam.GetComponentInChildren<ParticleSystem>().main.startColor.color);
                }
                Assert.That(colors.Distinct().Count(), Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void MissingVisualDoesNotLeaveBeamOnRecycledPickup()
        {
            var view = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ResourcePickupView>(Folder + "TestResourcePickup.prefab"));
            try
            {
                view.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "ArcadeDropCoin.prefab"), "Prefabs/SceneObjects/ArcadeDropCoin");
                Assert.That(view.transform.Find("ArcadeLootBeam"), Is.Not.Null);
                Assert.That(view.SetArcadePresentation(null, null), Is.False);
                Assert.That(view.transform.Find("ArcadeLootBeam"), Is.Null);
                Assert.That(view.transform.Find("ArcadeDropVisual"), Is.Null);
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void ShieldBlockFlashesOwnedInstanceAndRestoresItsMaterial()
        {
            var root = new GameObject("ShieldTestRoot");
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "ArcadeShieldEffect.prefab"), root.transform);
                var renderer = instance.GetComponentInChildren<Renderer>();
                var originalColor = renderer.sharedMaterial.GetColor("_BaseColor");
                var view = new ArcadeCombatViewController();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(ArcadeCombatViewController).GetNestedType("EffectVisualEntry", System.Reflection.BindingFlags.NonPublic);
                var entry = System.Activator.CreateInstance(type, true);
                type.GetField("Instance", flags).SetValue(entry, instance);
                var key = "powerup:7:" + (int)ArcadePowerupBehavior.Shield;
                type.GetField("Key", flags).SetValue(entry, key);
                var entries = (System.Collections.IDictionary)typeof(ArcadeCombatViewController).GetField("effectVisuals", flags).GetValue(view);
                entries.Add(key, entry);
                view.PlayShieldBlock(7, Vector2.one);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(properties.GetColor("_BaseColor").a, Is.GreaterThan(0.7f));
                Assert.That(instance.transform.localScale.x, Is.GreaterThan(1f));
                view.TickPlayerFeedback(1f);
                renderer.GetPropertyBlock(properties);
                Assert.That(properties.isEmpty, Is.True);
                Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(originalColor));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ImportedEffectsUseUrpMaterialsAndOnlyOwnedDependencies()
        {
            foreach (var name in new[] { "ArcadeLootBeam", "ArcadeDropLightning", "ArcadeShieldEffect" })
            {
                var path = Folder + name + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, name);
                Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true), Is.Not.Empty, name);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, name);
                Assert.That(AssetDatabase.GetDependencies(path, true).Any(p => p.StartsWith("Assets/Art/")), Is.False, name);
                foreach (var material in prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null))
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"), name + ": " + material.name);
            }
        }
    }
}

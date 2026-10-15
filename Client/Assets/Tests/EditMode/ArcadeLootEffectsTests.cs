using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AnotherDay.Tests
{
    public sealed class ArcadeLootEffectsTests
    {
        private const string Folder = "Assets/Game/Prefabs/SceneObjects/";

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

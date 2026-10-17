using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证特殊枪弹独立外观与爆炸池；不推进伤害或更改权威规则。</summary>
    public sealed class ArcadeSpecialWeaponEffectsTests
    {
        [Test]
        public void PiercingBeamHasSoftEmissiveTextureAndStaysThinAlongTravelAxis()
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"));
            try
            {
                go.GetComponent<ProjectileView>().SetArcadePresentation("ArcadePiercingRay", false);
                var root = go.transform.Find("ArcadePiercingRay");
                var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                Assert.That(renderers, Is.Not.Empty);
                foreach (var renderer in renderers)
                {
                    Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null, "穿透弹需光晕衰减，不能以实心方块呈现");
                    var size = renderer.bounds.size;
                    Assert.That(size.z, Is.GreaterThan(Mathf.Max(size.x, size.y) * 4), "光束长轴必须沿弹丸前进方向");
                    Assert.That(Mathf.Max(size.x, size.y), Is.LessThan(.2f), "穿透弹需保留细束辨识");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RaygunUsesIndependentBoltAndPoolReuseRestoresOtherVisuals()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + n + ".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var weapon = catalog.Weapons.Single(w => w.Id == "arcade.weapon.raygun");
            Assert.That(weapon.ProjectileVisualResourceName, Is.EqualTo("ArcadeRaygunBolt"));
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"));
            try
            {
                var view = go.GetComponent<ProjectileView>();
                Assert.That(view.Validate(out error), Is.True, error);
                view.SetArcadePresentation(weapon.ProjectileVisualResourceName, false);
                var bolt = go.transform.Find("ArcadeRaygunBolt");
                Assert.That(bolt, Is.Not.Null); Assert.That(bolt.gameObject.activeSelf, Is.True);
                Assert.That(bolt.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterial.shader.name.Contains("Universal Render Pipeline")), Is.True);
                view.SetArcadePresentation("ArcadePiercingRay", false);
                Assert.That(bolt.gameObject.activeSelf, Is.False);
                var piercing = go.transform.Find("ArcadePiercingRay");
                Assert.That(piercing, Is.Not.Null);
                Assert.That(piercing.gameObject.activeSelf, Is.True, "复用须选中正式穿透视觉根");
                Assert.That(piercing.GetComponentsInChildren<Renderer>(), Is.Not.Empty);
                view.SetArcadePresentation(weapon.ProjectileVisualResourceName, true);
                Assert.That(bolt.gameObject.activeSelf, Is.False, "敌弹复用不能继承玩家能量弹");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ExplosionsRemainVisibleDuringAudioCooldownAndReuseBoundedMapPool()
        {
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            Assert.That(kit.transform.Find("Content/ProjectileBlast"), Is.Not.Null, "正式资源租约需包含爆炸模板");
            var prior = ResourceLoader.Current; var root = new GameObject("special-impact-owner");
            var view = new ArcadeCombatViewController();
            ResourceLoader.Use(new AssetLoader());
            Set(view, "root", root); Set(view, "mapRoot", root.transform);
            Set(view, "lastExplosionSoundTime", float.MaxValue); Set(view, "lastSciFiExplosionSoundTime", float.MaxValue);
            try
            {
                typeof(ArcadeCombatViewController).GetMethod("LoadContentAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { 15 });
                for (int i = 0; i < 40; i++) view.PlayProjectileExplosion(new Vector2(i, 2), i % 2 == 0 ? 1.35f : 2.8f);
                var pool = (IList)Get(view, "projectileBlastPool");
                Assert.That(pool.Count, Is.EqualTo(12)); Assert.That(root.transform.childCount, Is.EqualTo(12));
                for (int i = 0; i < 12; i++) view.PlayProjectileExplosion(new Vector2(2, 4), 1.35f);
                foreach (var particles in root.GetComponentsInChildren<ParticleSystem>())
                {
                    Assert.That(particles.main.loop, Is.False);
                    Assert.That(particles.GetComponentsInChildren<Collider>(), Is.Empty);
                    if (particles.name == "Burst" || particles.name == "Smoke") Assert.That(particles.isPlaying, Is.False);
                    else { Assert.That(particles.isPlaying, Is.True); Assert.That(particles.main.startColor.color.b, Is.GreaterThan(particles.main.startColor.color.r)); }
                }
                for (int i = 0; i < 12; i++) view.PlayProjectileExplosion(Vector2.zero, 3.2f);
                Assert.That(root.GetComponentsInChildren<ParticleSystem>().Where(p => p.name == "Burst").All(p => p.isPlaying), Is.True, "普通爆炸复用须恢复火焰层");
                view.End(); Assert.That(pool.Count, Is.Zero);
                view.PlayProjectileExplosion(Vector2.one, 2.8f); Assert.That(pool.Count, Is.Zero, "离场后不补播");
            }
            finally { view.End(); if (root != null) Object.DestroyImmediate(root); ResourceLoader.Use(prior); }
        }

        static object Get(object owner, string field) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        sealed class AssetLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object { callback(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}

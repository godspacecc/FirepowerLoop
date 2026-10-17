using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>定向验证普通枪弹可读性、命中复用和既有物理契约。</summary>
    public sealed class ArcadeGunfirePresentationTests
    {
        private const string Art = "Assets/Game/Art/Arcade/VFX/Gunfire/";

        [Test]
        public void OrdinaryBulletHasBrightShortStreakWithoutChangingCollider()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var renderer = instance.GetComponent<MeshRenderer>();
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial), Is.EqualTo(Art + "BulletStreak.mat"));
                Assert.That(renderer.bounds.size.z, Is.InRange(0.6f, 1.2f));
                Assert.That(renderer.bounds.size.x, Is.InRange(0.1f, 0.25f));
                Assert.That(instance.transform.localScale, Is.EqualTo(new Vector3(.08f, .08f, .22f)));
                Assert.That(instance.GetComponent<BoxCollider>().size, Is.EqualTo(Vector3.one));
                Assert.That(instance.GetComponent<Rigidbody>().isKinematic, Is.True);
                Assert.That(renderer.sharedMaterial.shader.name, Does.StartWith("Universal Render Pipeline/"));
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor").maxColorComponent, Is.GreaterThan(1f));
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(2), "亮芯与短尾共用同一 Renderer 的两个子网格");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void MuzzleUsesSelectedWarFxTextureWithoutThirdPartyScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/FX/PlayerMuzzleFlash.prefab");
            var particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(particles, Is.Not.Empty);
            var material = particles[0].GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(Art + "Textures/GunMuzzleFront.png"));
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        }

        [Test]
        public void ImpactTemplateIsShortBurstWithoutGameplayComponents()
        {
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            var template = kit.transform.Find("Content/BulletImpact");
            Assert.That(template, Is.Not.Null, "命中模板必须随既有场地资源准备");
            var particles = template.GetComponent<ParticleSystem>();
            Assert.That(particles, Is.Not.Null);
            Assert.That(particles.main.loop, Is.False);
            Assert.That(particles.main.startLifetime.constantMax, Is.LessThanOrEqualTo(.25f));
            Assert.That(template.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(template.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            var material = particles.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(Art + "Textures/GunImpactSpark.png"));
        }

        [Test]
        public void ImpactPoolIsBoundedAndClearedWithBattleOwner()
        {
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            var root = new GameObject("gunfire-test-owner");
            var view = new ArcadeCombatViewController();
            Set(view, "root", root);
            Set(view, "bulletImpactTemplate", kit.transform.Find("Content/BulletImpact").GetComponent<ParticleSystem>());
            try
            {
                var play = typeof(ArcadeCombatViewController).GetMethod("PlayBulletImpact", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(play, Is.Not.Null);
                for (int i = 0; i < 40; i++) play.Invoke(view, new object[] { new Vector2(i, 2f) });
                var pool = (IList)typeof(ArcadeCombatViewController).GetField("bulletImpactPool", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                Assert.That(pool.Count, Is.EqualTo(12));
                Assert.That(root.transform.childCount, Is.EqualTo(12));
                typeof(ArcadeCombatViewController).GetMethod("ClearBulletImpacts", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, null);
                Assert.That(pool.Count, Is.Zero);
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SelectedAssetsDoNotDependOnBackupLibrary()
        {
            string[] paths = { Art + "BulletStreak.mat", Art + "BulletCore.mat", Art + "MuzzleFlash.mat", Art + "ImpactSpark.mat" };
            foreach (var path in paths)
            {
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(path), Is.Not.Null);
                foreach (var dependency in AssetDatabase.GetDependencies(path, true))
                    Assert.That(dependency, Does.Not.StartWith("Assets/Art/"));
            }
        }

        [Test]
        public void LaterArenaLoadsImpactWithoutReplacingItsEnvironment()
        {
            var previous = ResourceLoader.Current;
            var root = new GameObject("late-arena-gunfire-owner");
            var view = new ArcadeCombatViewController();
            Set(view, "root", root);
            ResourceLoader.Use(new GunfireAssetLoader());
            try
            {
                typeof(ArcadeCombatViewController).GetMethod("LoadContentAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(view, new object[] { 15 });
                Assert.That(typeof(ArcadeCombatViewController).GetField("contentEnvironment", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view), Is.Null,
                    "公共模板可准备，但后续竞技场不应创建前三场的环境覆盖实例");
                typeof(ArcadeCombatViewController).GetMethod("PlayBulletImpact", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(view, new object[] { Vector2.zero });
                Assert.That(root.GetComponentsInChildren<ParticleSystem>().Length, Is.EqualTo(1), "后续竞技场仍需命中火星");
            }
            finally
            {
                view.End();
                Object.DestroyImmediate(root);
                ResourceLoader.Use(previous);
            }
        }

        private sealed class GunfireAssetLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> callback) where T : Object
            {
                callback(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }
            public void Release(ResourceLoadHandle handle) { }
        }

        private static void Set(object owner, string field, object value) =>
            owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
    }
}

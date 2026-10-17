using System;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>用正式共享武器、粒子和投射物验证首批三枪的表现差异与回收。</summary>
    public sealed class ArcadeGunfireStyleTests
    {
        private static readonly string[] Ids = { "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.spread" };

        [Test]
        public void SharedMuzzlesFollowVisibleBarrelsDuringFire()
        {
            foreach (var id in Ids)
            {
                var go = Player();
                try
                {
                    var view = go.GetComponent<PlayerView>();
                    var presenter = go.GetComponent<CharacterWeaponPresenter>();
                    Bind(view, id);
                    foreach (float time in new[] { 0f, .25f, .5f, .75f })
                    {
                        presenter.BodyAnimator.Play("Base Layer.Attack", 0, time);
                        presenter.BodyAnimator.Update(.001f);
                        var visual = presenter.Visual.transform;
                        var points = visual.GetComponentsInChildren<MeshFilter>(true)
                            .SelectMany(m => m.sharedMesh.vertices.Select(v => visual.InverseTransformPoint(m.transform.TransformPoint(v)))).ToArray();
                        float front = points.Max(v => v.z);
                        var tip = points.Where(v => v.z >= front - .02f).ToArray();
                        var center = tip.Aggregate(Vector3.zero, (a, b) => a + b) / tip.Length;
                        Assert.That(Vector3.Distance(view.GetProjectileOrigin(Vector3.forward), visual.TransformPoint(center)), Is.LessThan(.1f), id);
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        [Test]
        public void ThreeGunsHaveDistinctMuzzleBursts()
        {
            var sizes = new float[3]; var lifetimes = new float[3];
            var go = Player();
            try
            {
                var view = go.GetComponent<PlayerView>();
                for (int i = 0; i < Ids.Length; i++)
                {
                    Bind(view, Ids[i]); view.PlayProjectileFireFeedback();
                    var ps = (ParticleSystem)typeof(PlayerView).GetField("muzzleFlash", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                    sizes[i] = ps.main.startSize.constantMax; lifetimes[i] = ps.main.startLifetime.constantMax;
                    Assert.That(ps.main.maxParticles, Is.LessThanOrEqualTo(4));
                }
                Assert.That(sizes[1], Is.LessThan(sizes[0]), "连射闪光应更克制");
                Assert.That(sizes[2], Is.GreaterThan(sizes[0]), "散射应有更宽闪光");
                Assert.That(lifetimes[1], Is.LessThan(lifetimes[2]));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SharedGunKickReturnsAndDoesNotAccumulate()
        {
            var go = Player();
            try
            {
                var view = go.GetComponent<PlayerView>(); var presenter = go.GetComponent<CharacterWeaponPresenter>();
                Bind(view, Ids[2]);
                presenter.ApplyOffhandGrip(); var rest = presenter.Visual.transform.localPosition;
                view.PlayProjectileFireFeedback(); presenter.ApplyOffhandGrip();
                var kick = presenter.Visual.transform.localPosition;
                Assert.That(Vector3.Distance(rest, kick), Is.GreaterThan(.015f));
                presenter.ApplyOffhandGrip();
                Assert.That(Vector3.Distance(kick, presenter.Visual.transform.localPosition), Is.LessThan(.001f));
                presenter.TickUpperBodyGunFire(1f); presenter.ApplyOffhandGrip();
                Assert.That(Vector3.Distance(rest, presenter.Visual.transform.localPosition), Is.LessThan(.001f));
                view.PlayProjectileFireFeedback(); Bind(view, Ids[0]); presenter.ApplyOffhandGrip();
                var changed = presenter.Visual.transform.localPosition;
                presenter.TickUpperBodyGunFire(1f); presenter.ApplyOffhandGrip();
                Assert.That(Vector3.Distance(changed, presenter.Visual.transform.localPosition), Is.LessThan(.001f), "换枪不得带入旧后坐");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GeneratedWeaponsSelectDistinctBulletVisualsWithoutChangingCollider()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(System.IO.File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var names = Ids.Select(id => catalog.Weapons.Single(w => w.Id == id).ProjectileVisualResourceName).ToArray();
            Assert.That(names.All(n => !string.IsNullOrWhiteSpace(n)), Is.True);
            Assert.That(names.Distinct().Count(), Is.EqualTo(3));
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab"));
            try
            {
                foreach (var name in names)
                {
                    go.GetComponent<ProjectileView>().SetArcadePresentation(name, false);
                    Assert.That(go.GetComponent<Renderer>().enabled, Is.False);
                    Assert.That(go.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled), Is.EqualTo(1));
                }
                go.GetComponent<ProjectileView>().SetArcadePresentation(null, false);
                Assert.That(go.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(go.GetComponent<BoxCollider>().size, Is.EqualTo(Vector3.one));
                Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(.08f, .08f, .22f)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static GameObject Player() => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));

        [Test]
        public void SurfaceSparkUsesSharedBoundedPoolAndResetsForBodyHit()
        {
            var root = new GameObject("surface-spark-owner"); var view = new ArcadeCombatViewController();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(ArcadeCombatViewController).GetField("root", flags).SetValue(view, root);
            typeof(ArcadeCombatViewController).GetField("mapRoot", flags).SetValue(view, root.transform);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/BulletImpact").GetComponent<ParticleSystem>();
            typeof(ArcadeCombatViewController).GetField("bulletImpactTemplate", flags).SetValue(view, template);
            try
            {
                var surface = typeof(ArcadeCombatViewController).GetMethod("PlayProjectileSurfaceImpact");
                Assert.That(surface, Is.Not.Null, "墙面接触需有独立表现入口");
                for (int i = 0; i < 13; i++) surface.Invoke(view, new object[] { new Vector2(2, 3), Vector2.right });
                var particles = root.GetComponentsInChildren<ParticleSystem>();
                Assert.That(particles.Length, Is.EqualTo(12));
                Assert.That(particles[1].shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Cone));
                typeof(ArcadeCombatViewController).GetMethod("PlayBulletImpact", flags).Invoke(view, new object[] { Vector2.zero });
                Assert.That(particles[1].shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Sphere), "共池复用必须恢复命中敌人的球形火星");
                Assert.That(particles[1].transform.localRotation, Is.EqualTo(Quaternion.identity));
            }
            finally { view.End(); Object.DestroyImmediate(root); }
        }
        private static void Bind(PlayerView view, string id)
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(System.IO.File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view, catalog.Weapons.Single(w => w.Id == id));
            view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.Animator.Update(.001f);
        }
    }
}

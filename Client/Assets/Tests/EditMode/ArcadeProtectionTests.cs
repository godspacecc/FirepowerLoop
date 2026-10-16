using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace FirepowerLoop.Tests
{
    /// <summary>验证真实玩家保护表现的状态同步和回收，不创建第二套保护计时。</summary>
    public sealed class ArcadeProtectionTests
    {
        private GameObject player;
        private PlayerView view;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("ProtectionPlayer");
            view = player.AddComponent<PlayerView>();
            view.SetArcadePresentationActive(true);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(player);

        [Test]
        public void EffectiveProtectionShowsHollowEffectUntilStateEnds()
        {
            SetProtection(true);
            var effect = player.transform.Find("ArcadeProtection");
            Assert.That(effect, Is.Not.Null, "有效保护必须立即可见，无需护盾配置或异步加载");
            Assert.That(effect.gameObject.activeSelf, Is.True);
            Assert.That(effect.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(effect.GetComponentsInChildren<LineRenderer>(), Is.Not.Empty);
            foreach (var line in effect.GetComponentsInChildren<LineRenderer>())
            {
                Assert.That(line.widthMultiplier, Is.LessThan(.08f));
                for (int index = 0; index < line.positionCount; index++)
                {
                    Vector3 point = line.GetPosition(index);
                    Assert.That(new Vector2(point.x, point.z).magnitude, Is.GreaterThan(.6f));
                    Assert.That(point.y, Is.LessThan(.2f));
                }
            }
            // 视觉反馈推进很久也不能自行结束实际仍有效的保护。
            view.TickCombatFeedback(100f);
            Assert.That(effect.gameObject.activeSelf, Is.True);
            SetProtection(true);
            Assert.That(player.transform.Find("ArcadeProtection"), Is.SameAs(effect));
            SetProtection(false);
            Assert.That(effect.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void DeathAndHiddenPlayerCannotLeaveFloatingEffect()
        {
            SetProtection(true);
            var effect = player.transform.Find("ArcadeProtection");
            Assert.That(effect, Is.Not.Null);
            view.SetAlivePresentation(false);
            Assert.That(effect.gameObject.activeSelf, Is.False);
            SetProtection(true);
            Assert.That(effect.gameObject.activeSelf, Is.False);
            view.SetAlivePresentation(true);
            SetProtection(true);
            player.SetActive(false);
            Assert.That(effect.gameObject.activeInHierarchy, Is.False);
            // EditMode 不自动派发普通 MonoBehaviour 的禁用回调，显式执行正式回调验证清理。
            typeof(PlayerView).GetMethod("OnDisable", Flags).Invoke(view, null);
            player.SetActive(true);
            Assert.That(effect.gameObject.activeSelf, Is.False, "重新显示不能沿用隐藏前的保护状态");
        }

        [Test]
        public void LeavingArcadeReleasesEffectAndOwnedMaterial()
        {
            SetProtection(true);
            var effect = player.transform.Find("ArcadeProtection");
            Assert.That(effect, Is.Not.Null);
            var material = effect.GetComponentInChildren<Renderer>().sharedMaterial;
            view.SetArcadePresentationActive(false);
            Assert.That(effect == null, Is.True);
            Assert.That(material == null, Is.True);
            view.SetArcadePresentationActive(true);
            Assert.That(player.transform.Find("ArcadeProtection"), Is.Null);
            SetProtection(true);
            Assert.That(player.transform.Find("ArcadeProtection"), Is.Not.Null);
        }

        [TestCase(1f, 2f)]
        [TestCase(2f, 1f)]
        public void EarlierSourceEndingCannotClearRemainingEffectiveProtection(float invulnerability, float dash)
        {
            WithCombat((system, runtime, _) =>
            {
                runtime.PlayerInvulnerability = invulnerability;
                runtime.DashRemaining = dash;
                Call(system, "SyncView", runtime);
                var effect = player.transform.Find("ArcadeProtection");
                Assert.That(effect, Is.Not.Null);
                Call(system, "TickTimers", runtime, 1f);
                Call(system, "SyncView", runtime);
                Assert.That(effect.gameObject.activeSelf, Is.True);
                Call(system, "DamagePlayer", runtime, 5);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
                Call(system, "TickTimers", runtime, 1f);
                Call(system, "SyncView", runtime);
                Assert.That(effect.gameObject.activeSelf, Is.False);
                Call(system, "DamagePlayer", runtime, 5);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(95));
            });
        }

        [Test]
        public void RespawnKeepsConfiguredDurationAndDamageBoundary()
        {
            WithCombat((system, runtime, catalog) =>
            {
                runtime.PlayerHealth = 0;
                runtime.RespawnRemaining = .1f;
                Call(system, "SyncView", runtime);
                Assert.That(player.transform.Find("ArcadeProtection"), Is.Null);
                Call(system, "TickTimers", runtime, .1f);
                Assert.That(runtime.PlayerInvulnerability, Is.EqualTo(catalog.Rule.ContactInvulnerabilitySeconds * 2f));
                Call(system, "SyncView", runtime);
                var effect = player.transform.Find("ArcadeProtection");
                Assert.That(effect, Is.Not.Null);
                Assert.That(effect.gameObject.activeSelf, Is.True);
                Call(system, "DamagePlayer", runtime, 5);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
                float remaining = runtime.PlayerInvulnerability;
                Call(system, "TickTimers", runtime, remaining / 2f);
                Call(system, "SyncView", runtime);
                Assert.That(effect.gameObject.activeSelf, Is.True);
                Call(system, "TickTimers", runtime, remaining / 2f);
                Call(system, "SyncView", runtime);
                Assert.That(effect.gameObject.activeSelf, Is.False);
                Call(system, "DamagePlayer", runtime, 5);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(95));
            });
        }

        /// <summary>读取真实配置并执行正式规则、同步入口，只替代地图和非本次目标的外部表现。</summary>
        private void WithCombat(Action<ArcadeCombatSystem, ArcadeCombatRuntime, ArcadeConfigCatalogDefinition> action)
        {
            var architecture = new Game.Game();
            var run = new ArcadeRunModel();
            architecture.RegisterModel(run);
            run.CommitSafeNode(new ArcadeRunSaveData { RemainingLives = 3 });
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            typeof(ArcadeCombatSystem).GetField("catalog", Flags).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("view", Flags).SetValue(system, new PlayerPresentationPort(view));
            action(system, new ArcadeCombatRuntime { PlayerHealth = 100, PlayerMaxHealth = 100, PresentedRegion = 0 }, catalog);
        }

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Call(ArcadeCombatSystem system, string method, params object[] args) =>
            typeof(ArcadeCombatSystem).GetMethod(method, Flags).Invoke(system, args);

        private sealed class PlayerPresentationPort : IArcadeCombatViewPort
        {
            private readonly PlayerView view;
            public PlayerPresentationPort(PlayerView view) => this.view = view;
            public bool IsActive => true;
            public void Begin(int arena, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() => view.SetArcadePresentationActive(false);
            public bool TryScreenToArena(Vector2 point, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable)
            {
                view.SetAlivePresentation(visible);
                view.SetArcadeProtectionActive(visible && invulnerable);
            }
            public void SetPlayerHealth(int current, int maximum) { }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum,
                bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible,
                bool invulnerable, int current, int maximum) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float seconds) => view.TickCombatFeedback(seconds);
        }

        private void SetProtection(bool active) => view.SetArcadeProtectionActive(active);
    }
}
